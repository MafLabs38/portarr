//! A minimal HTTP/1.1 GET client for the Docker Engine API, over a Unix socket or plain TCP. The reader asks Docker for a handful of
//! fixed paths and never sends a body, so this is a few dozen lines instead of a dependency: one request per connection
//! (`Connection: close`), the body read as a chunked stream, a fixed length, or until the connection ends.

use std::io::{self, BufRead, BufReader, Read, Write};
use std::net::{TcpStream, ToSocketAddrs};
use std::os::unix::net::UnixStream;
use std::sync::mpsc;
use std::thread;
use std::time::Duration;

use crate::config::DockerEndpoint;

/// How long Docker may take to connect and to answer a request that is not a stream: a hung call must not hold a thread for ever.
pub const CALL_TIMEOUT: Duration = Duration::from_secs(10);

/// Largest body read into memory (a container list, a stats sample): more is not what Docker answers.
pub const MAX_BODY_BYTES: u64 = 32 * 1024 * 1024;

const MAX_HEADER_BYTES: usize = 64 * 1024;

/// How long a followed log stays open. A caller that left on a quiet container is only noticed when the container writes again, so the
/// connection to Docker is closed after this long: the thread and its slot come back, and a viewer that is still there follows again.
pub const MAX_FOLLOW: Duration = Duration::from_secs(30 * 60);

trait Transport: Read + Write + Send {}
impl<T: Read + Write + Send> Transport for T {}

/// An answer from Docker: its status, and its body as a stream (already unframed, whatever the transfer encoding).
pub struct Upstream {
    pub status: u16,
    body: Box<dyn Read + Send>,
}

impl Upstream {
    pub fn into_body(self) -> Box<dyn Read + Send> {
        self.body
    }

    /// The whole body, refusing more than [`MAX_BODY_BYTES`].
    pub fn read_all(self) -> io::Result<Vec<u8>> {
        let mut bytes = Vec::new();
        self.body.take(MAX_BODY_BYTES + 1).read_to_end(&mut bytes)?;
        if bytes.len() as u64 > MAX_BODY_BYTES {
            return Err(io::Error::new(io::ErrorKind::InvalidData, "the answer is larger than the reader accepts"));
        }
        Ok(bytes)
    }
}

/// `GET target`, where `target` is a path and query built by the reader from validated values. `read_timeout` bounds each read
/// (none for a followed log, which waits for lines).
pub fn get(endpoint: &DockerEndpoint, target: &str, read_timeout: Option<Duration>) -> io::Result<Upstream> {
    if target.bytes().any(|byte| byte <= b' ' || byte == 0x7f) {
        return Err(io::Error::new(io::ErrorKind::InvalidInput, "a request target holds a space or a control character"));
    }

    let mut alive: Option<mpsc::Sender<()>> = None;
    let (stream, host): (Box<dyn Transport>, String) = match endpoint {
        DockerEndpoint::Unix(path) => {
            let stream = UnixStream::connect(path)?;
            stream.set_read_timeout(read_timeout)?;
            stream.set_write_timeout(Some(CALL_TIMEOUT))?;
            if read_timeout.is_none() {
                let closing = stream.try_clone()?;
                close_after(MAX_FOLLOW, &mut alive, move || drop(closing.shutdown(std::net::Shutdown::Both)));
            }
            (Box::new(stream), "docker".to_string())
        }
        DockerEndpoint::Tcp { host, port } => {
            let address = (host.as_str(), *port)
                .to_socket_addrs()?
                .next()
                .ok_or_else(|| io::Error::new(io::ErrorKind::NotFound, "the Docker host does not resolve"))?;
            let stream = TcpStream::connect_timeout(&address, CALL_TIMEOUT)?;
            stream.set_read_timeout(read_timeout)?;
            stream.set_write_timeout(Some(CALL_TIMEOUT))?;
            if read_timeout.is_none() {
                let closing = stream.try_clone()?;
                close_after(MAX_FOLLOW, &mut alive, move || drop(closing.shutdown(std::net::Shutdown::Both)));
            }
            (Box::new(stream), format!("{host}:{port}"))
        }
    };

    let mut reader = BufReader::new(stream);
    write!(
        reader.get_mut(),
        "GET {target} HTTP/1.1\r\nHost: {host}\r\nUser-Agent: portarr-reader\r\nAccept: */*\r\nConnection: close\r\n\r\n"
    )?;
    reader.get_mut().flush()?;

    let mut line = String::new();
    read_line(&mut reader, &mut line)?;
    let status = line
        .split_whitespace()
        .nth(1)
        .and_then(|code| code.parse::<u16>().ok())
        .ok_or_else(|| io::Error::new(io::ErrorKind::InvalidData, "not an HTTP status line"))?;

    let (mut chunked, mut length) = (false, None);
    let mut header_bytes = 0;
    loop {
        line.clear();
        read_line(&mut reader, &mut line)?;
        header_bytes += line.len();
        if header_bytes > MAX_HEADER_BYTES {
            return Err(io::Error::new(io::ErrorKind::InvalidData, "the response headers are too large"));
        }
        let header = line.trim_end();
        if header.is_empty() {
            break;
        }
        if let Some((name, value)) = header.split_once(':') {
            let value = value.trim();
            if name.eq_ignore_ascii_case("transfer-encoding") && value.to_ascii_lowercase().contains("chunked") {
                chunked = true;
            } else if name.eq_ignore_ascii_case("content-length") {
                length = value.parse::<u64>().ok();
            }
        }
    }

    let body: Box<dyn Read + Send> = if status == 204 || status == 304 {
        Box::new(io::empty())
    } else if chunked {
        Box::new(ChunkedReader::new(reader))
    } else if let Some(length) = length {
        Box::new(reader.take(length))
    } else {
        Box::new(reader)
    };
    // The sender lives as long as the body: when the body is dropped the watchdog wakes up and leaves.
    let body: Box<dyn Read + Send> = match alive {
        Some(alive) => Box::new(Watched { inner: body, _alive: alive }),
        None => body,
    };
    Ok(Upstream { status, body })
}

/// Calls `close` once `limit` has passed, unless the body it watches is dropped first.
fn close_after(limit: Duration, alive: &mut Option<mpsc::Sender<()>>, close: impl FnOnce() + Send + 'static) {
    let (sender, watcher) = mpsc::channel::<()>();
    let spawned = thread::Builder::new().stack_size(64 * 1024).spawn(move || {
        if watcher.recv_timeout(limit) == Err(mpsc::RecvTimeoutError::Timeout) {
            close();
        }
    });
    if spawned.is_ok() {
        *alive = Some(sender);
    }
}

/// A body whose connection a watchdog may close; dropping it releases the watchdog.
struct Watched {
    inner: Box<dyn Read + Send>,
    _alive: mpsc::Sender<()>,
}

impl Read for Watched {
    fn read(&mut self, buffer: &mut [u8]) -> io::Result<usize> {
        self.inner.read(buffer)
    }
}

fn read_line(reader: &mut impl BufRead, line: &mut String) -> io::Result<()> {
    if reader.take(MAX_HEADER_BYTES as u64).read_line(line)? == 0 {
        return Err(io::Error::new(io::ErrorKind::UnexpectedEof, "the connection closed before the response was complete"));
    }
    Ok(())
}

/// Decodes `Transfer-Encoding: chunked`: each read gives bytes of the current chunk, so a followed log is passed on as it arrives.
pub struct ChunkedReader<R> {
    inner: R,
    remaining: u64,
    done: bool,
    expect_chunk_end: bool,
}

impl<R: BufRead> ChunkedReader<R> {
    pub fn new(inner: R) -> ChunkedReader<R> {
        ChunkedReader {
            inner,
            remaining: 0,
            done: false,
            expect_chunk_end: false,
        }
    }

    fn next_chunk_size(&mut self) -> io::Result<u64> {
        let mut line = String::new();
        read_line(&mut self.inner, &mut line)?;
        let size = line.trim().split(';').next().unwrap_or_default();
        u64::from_str_radix(size, 16).map_err(|_| io::Error::new(io::ErrorKind::InvalidData, "a chunk size is not hexadecimal"))
    }

    fn skip_line(&mut self) -> io::Result<String> {
        let mut line = String::new();
        read_line(&mut self.inner, &mut line)?;
        Ok(line)
    }
}

impl<R: BufRead> Read for ChunkedReader<R> {
    fn read(&mut self, buffer: &mut [u8]) -> io::Result<usize> {
        if self.done || buffer.is_empty() {
            return Ok(0);
        }
        if self.remaining == 0 {
            if self.expect_chunk_end {
                self.skip_line()?;
            }
            self.remaining = self.next_chunk_size()?;
            self.expect_chunk_end = true;
            if self.remaining == 0 {
                // The last chunk, then optional trailers up to an empty line.
                while !self.skip_line()?.trim().is_empty() {}
                self.done = true;
                return Ok(0);
            }
        }

        let wanted = buffer.len().min(usize::try_from(self.remaining).unwrap_or(usize::MAX));
        let read = self.inner.read(&mut buffer[..wanted])?;
        if read == 0 {
            return Err(io::Error::new(io::ErrorKind::UnexpectedEof, "the connection closed inside a chunk"));
        }
        self.remaining -= read as u64;
        Ok(read)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn decode(framed: &[u8]) -> io::Result<Vec<u8>> {
        let mut out = Vec::new();
        ChunkedReader::new(framed).read_to_end(&mut out)?;
        Ok(out)
    }

    #[test]
    fn chunks_are_joined_and_extensions_and_trailers_ignored() {
        assert_eq!(decode(b"5\r\nhello\r\n6;ext=1\r\n world\r\n0\r\nTrailer: x\r\n\r\n").unwrap(), b"hello world");
        assert_eq!(decode(b"0\r\n\r\n").unwrap(), b"");
    }

    #[test]
    fn a_chunk_cut_short_or_a_bad_size_is_an_error_not_a_hang() {
        assert!(decode(b"5\r\nhel").is_err());
        assert!(decode(b"zz\r\nhello\r\n").is_err());
        assert!(decode(b"5\r\nhello\r\n").is_err(), "no last chunk");
    }

    #[test]
    fn a_watchdog_closes_a_connection_left_open_but_not_one_that_ended() {
        use std::sync::atomic::{AtomicBool, Ordering};
        use std::sync::Arc;

        let closed = Arc::new(AtomicBool::new(false));
        let mut alive = None;
        let flag = Arc::clone(&closed);
        close_after(Duration::from_millis(50), &mut alive, move || flag.store(true, Ordering::SeqCst));
        std::thread::sleep(Duration::from_millis(400));
        assert!(closed.load(Ordering::SeqCst), "left open: closed after the limit");
        drop(alive);

        let ended = Arc::new(AtomicBool::new(false));
        let mut alive = None;
        let flag = Arc::clone(&ended);
        close_after(Duration::from_millis(300), &mut alive, move || flag.store(true, Ordering::SeqCst));
        drop(alive);
        std::thread::sleep(Duration::from_millis(600));
        assert!(!ended.load(Ordering::SeqCst), "the body was dropped first: nothing to close");
    }

    #[test]
    fn a_target_with_a_space_or_a_control_character_is_never_sent() {
        let endpoint = DockerEndpoint::Unix("/nonexistent.sock".to_string());

        for target in ["/info HTTP/1.1\r\nX: y", "/a b", "/a\nb", "/a\u{7f}"] {
            let error = get(&endpoint, target, None).err().unwrap();
            assert_eq!(error.kind(), io::ErrorKind::InvalidInput, "{target:?}");
        }
    }
}
