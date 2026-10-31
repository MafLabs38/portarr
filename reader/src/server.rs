//! The reader's HTTP surface (D-100, D-102): one token for everything but `/healthz`, only GET routed, and no route for anything the
//! reader does not serve. A request that matches no route is a 404, a known route with another verb a 405.

use std::io::{self, Cursor, Read};
use std::net::SocketAddr;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::Arc;
use std::thread::{self, JoinHandle};

use serde_json::json;
use tiny_http::{Header, Method, Request, Response, StatusCode};

use crate::auth::Authenticator;
use crate::config::Options;
use crate::docker::{self, Docker, GatewayError, LogQuery, MAX_LOG_LINES};
use crate::host;

/// The version reported by `/v1/info`: the one given at build time, else the crate's.
pub const VERSION: &str = match option_env!("PORTARR_READER_VERSION") {
    Some(version) => version,
    None => env!("CARGO_PKG_VERSION"),
};

pub const HEALTH_PATH: &str = "/healthz";

/// Requests served at once: a followed log holds one for as long as it runs, and a flood must not exhaust the machine's threads.
const MAX_IN_FLIGHT: usize = 128;

/// A request thread holds a buffer and a few strings: a small stack keeps the memory of a busy reader low.
const THREAD_STACK_BYTES: usize = 256 * 1024;

type Reply = Response<Box<dyn Read + Send>>;

/// What a request is answered with: a complete reply, or a log stream written chunk by chunk.
enum Outcome {
    Reply(Reply),
    Stream(Box<dyn Read + Send>),
}

struct Context {
    authenticator: Authenticator,
    docker: Docker,
    proc_path: String,
    sys_path: String,
    host_name: String,
}

pub struct RunningServer {
    pub address: SocketAddr,
    server: Arc<tiny_http::Server>,
    acceptor: JoinHandle<()>,
}

impl RunningServer {
    /// Blocks while the server runs (until [`RunningServer::stop`] is called from another thread).
    pub fn wait(self) {
        let _ = self.acceptor.join();
    }

    pub fn stop(self) {
        self.server.unblock();
        let _ = self.acceptor.join();
    }
}

pub fn start(options: Options) -> io::Result<RunningServer> {
    let server = tiny_http::Server::http(options.listen).map_err(|error| io::Error::other(error.to_string()))?;
    let address = server
        .server_addr()
        .to_ip()
        .ok_or_else(|| io::Error::other("the server does not listen on an IP address"))?;
    let server = Arc::new(server);

    let host_name = options.host_name.clone().unwrap_or_else(|| machine_name(&options.proc_path));
    let context = Arc::new(Context {
        authenticator: Authenticator::new(&options.token),
        docker: Docker::new(options.docker),
        proc_path: options.proc_path,
        sys_path: options.sys_path,
        host_name,
    });
    let in_flight = Arc::new(AtomicUsize::new(0));

    let accepting = Arc::clone(&server);
    let acceptor = thread::spawn(move || {
        for request in accepting.incoming_requests() {
            if in_flight.fetch_add(1, Ordering::SeqCst) >= MAX_IN_FLIGHT {
                in_flight.fetch_sub(1, Ordering::SeqCst);
                let _ = request.respond(error(503, "Too many requests at once."));
                continue;
            }

            let (context, in_flight_done) = (Arc::clone(&context), Arc::clone(&in_flight));
            let spawned = thread::Builder::new().stack_size(THREAD_STACK_BYTES).spawn(move || {
                handle(request, &context);
                in_flight_done.fetch_sub(1, Ordering::SeqCst);
            });
            if spawned.is_err() {
                in_flight.fetch_sub(1, Ordering::SeqCst);
            }
        }
    });

    Ok(RunningServer { address, server, acceptor })
}

/// The machine's name: the kernel's, else a fixed word (it is only informative).
fn machine_name(proc_path: &str) -> String {
    std::fs::read_to_string(std::path::Path::new(proc_path).join("sys/kernel/hostname"))
        .map(|name| name.trim().to_string())
        .ok()
        .filter(|name| !name.is_empty())
        .unwrap_or_else(|| "unknown".to_string())
}

#[derive(Debug, PartialEq, Eq)]
enum Route<'a> {
    Health,
    Info,
    Host,
    DockerInfo,
    ContainerList,
    Json(&'a str),
    Stats(&'a str),
    Logs(&'a str),
}

fn route(path: &str) -> Option<Route<'_>> {
    match path {
        HEALTH_PATH => Some(Route::Health),
        "/v1/info" => Some(Route::Info),
        "/v1/host" => Some(Route::Host),
        "/v1/docker/info" => Some(Route::DockerInfo),
        "/v1/docker/containers/json" => Some(Route::ContainerList),
        _ => {
            let (name, action) = path.strip_prefix("/v1/docker/containers/")?.split_once('/')?;
            match action {
                "json" => Some(Route::Json(name)),
                "stats" => Some(Route::Stats(name)),
                "logs" => Some(Route::Logs(name)),
                _ => None,
            }
        }
    }
}

fn handle(request: Request, context: &Context) {
    match respond_to(&request, context) {
        Outcome::Reply(reply) => {
            let _ = request.respond(reply);
        }
        Outcome::Stream(body) => stream_logs(request, body),
    }
}

/// Writes a followed log as a chunked response and sends each chunk as it arrives. The HTTP library buffers a chunked body, which would
/// deliver a live log in batches (or at its end); this is the one route that needs the raw connection.
fn stream_logs(request: Request, mut body: Box<dyn Read + Send>) {
    use std::io::Write;

    let mut out = request.into_writer();
    if out
        .write_all(b"HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n")
        .and_then(|()| out.flush())
        .is_err()
    {
        return;
    }

    let mut buffer = [0u8; 16 * 1024];
    loop {
        match body.read(&mut buffer) {
            Ok(0) => break,
            Ok(read) => {
                if write!(out, "{read:x}\r\n")
                    .and_then(|()| out.write_all(&buffer[..read]))
                    .and_then(|()| out.write_all(b"\r\n"))
                    .and_then(|()| out.flush())
                    .is_err()
                {
                    return;
                }
            }
            // Docker's side broke: end without the closing chunk, so the caller sees a broken stream and not a log that ended cleanly.
            Err(_) => return,
        }
    }
    let _ = out.write_all(b"0\r\n\r\n").and_then(|()| out.flush());
}

fn respond_to(request: &Request, context: &Context) -> Outcome {
    let url = request.url();
    let (path, query) = url.split_once('?').unwrap_or((url, ""));

    // Everything but the health check needs the token, before the path is even looked at: an anonymous caller learns nothing about routes.
    if path != HEALTH_PATH
        && !context
            .authenticator
            .accepts(header(request, "Authorization").as_deref(), header(request, "X-Api-Key").as_deref())
    {
        return Outcome::Reply(with_header(empty(401), "WWW-Authenticate", "Bearer realm=\"portarr-reader\""));
    }

    let Some(route) = route(path) else { return Outcome::Reply(empty(404)) };
    if *request.method() != Method::Get {
        return Outcome::Reply(with_header(empty(405), "Allow", "GET"));
    }

    if let Route::Logs(name) = route {
        return logs(context, name, query);
    }
    Outcome::Reply(reply_for(route, context, query))
}

fn logs(context: &Context, name: &str, query: &str) -> Outcome {
    if !docker::is_valid_name(name) {
        return Outcome::Reply(error(400, "That is not a container name."));
    }
    match log_query(query) {
        Ok(log_query) => match context.docker.logs(name, log_query) {
            Ok(upstream) => Outcome::Stream(upstream.into_body()),
            Err(failure) => Outcome::Reply(failure_reply(&failure)),
        },
        Err(message) => Outcome::Reply(error(400, message)),
    }
}

fn reply_for(route: Route<'_>, context: &Context, query: &str) -> Reply {
    match route {
        Route::Health => text(200, "ok", "text/plain"),
        Route::Info => json_reply(
            200,
            json!({ "name": "portarr-reader", "version": VERSION, "host": context.host_name, "capabilities": ["host", "docker"] }).to_string(),
        ),
        Route::Host => match host::read_snapshot(&context.proc_path, &context.sys_path) {
            Ok(snapshot) => text(200, &host::exposition(&snapshot), host::CONTENT_TYPE),
            Err(_) => error(502, "The host's /proc could not be read."),
        },
        Route::DockerInfo => from_gateway(context.docker.info().map(String::into_bytes)),
        Route::ContainerList => match optional_bool(query, "all") {
            Some(all) => from_gateway(context.docker.list(all.unwrap_or(false)).map(String::into_bytes)),
            None => error(400, "all must be true or false."),
        },
        Route::Json(name) => with_container(name, |name| from_gateway(context.docker.inspect(name).map(String::into_bytes))),
        Route::Stats(name) => with_container(name, |name| {
            // Lenient on purpose: anything but "true" is not a one-shot request.
            let one_shot = query_value(query, "one-shot").is_some_and(|value| value.eq_ignore_ascii_case("true"));
            from_gateway(context.docker.stats(name, one_shot))
        }),
        Route::Logs(_) => error(500, "Logs are streamed, not replied."),
    }
}

fn with_container(name: &str, then: impl FnOnce(&str) -> Reply) -> Reply {
    if docker::is_valid_name(name) {
        then(name)
    } else {
        error(400, "That is not a container name.")
    }
}

/// The parameters of a logs request, each optional; one that is present and not valid is refused, never guessed.
fn log_query(query: &str) -> Result<LogQuery, &'static str> {
    let flag = |key: &str, default: bool| {
        optional_bool(query, key)
            .map(|value| value.unwrap_or(default))
            .ok_or("stdout, stderr, timestamps and follow must be true or false.")
    };
    let tail = match query_value(query, "tail") {
        None => 100,
        Some(value) => value.parse::<i64>().map_err(|_| "tail must be a number of lines.")?,
    };
    Ok(LogQuery {
        stdout: flag("stdout", true)?,
        stderr: flag("stderr", true)?,
        timestamps: flag("timestamps", false)?,
        tail: tail.clamp(0, MAX_LOG_LINES),
        follow: flag("follow", false)?,
    })
}

fn query_value<'a>(query: &'a str, key: &str) -> Option<&'a str> {
    query.split('&').find_map(|pair| {
        let (name, value) = pair.split_once('=').unwrap_or((pair, ""));
        (name == key).then_some(value)
    })
}

/// `Some(None)`: the parameter is absent; `Some(Some(value))`: a valid boolean; `None`: present and not a boolean.
fn optional_bool(query: &str, key: &str) -> Option<Option<bool>> {
    match query_value(query, key) {
        None => Some(None),
        Some(value) if value.eq_ignore_ascii_case("true") => Some(Some(true)),
        Some(value) if value.eq_ignore_ascii_case("false") => Some(Some(false)),
        Some(_) => None,
    }
}

fn header(request: &Request, name: &'static str) -> Option<String> {
    request
        .headers()
        .iter()
        .find(|header| header.field.equiv(name))
        .map(|header| header.value.as_str().to_string())
}

fn from_gateway(result: Result<Vec<u8>, GatewayError>) -> Reply {
    match result {
        Ok(body) => bytes(200, body, "application/json"),
        Err(failure) => failure_reply(&failure),
    }
}

/// Docker's not found stays a 404; anything else is the reader's bad gateway, never Docker's own text.
fn failure_reply(failure: &GatewayError) -> Reply {
    match failure {
        GatewayError::NotFound => empty(404),
        GatewayError::Refused => error(502, "Docker refused the request."),
        GatewayError::Unreachable => error(502, "Docker is not reachable."),
        GatewayError::Timeout => error(504, "Docker did not answer in time."),
        GatewayError::Unexpected => error(502, "Docker answered something unexpected."),
    }
}

fn bytes(status: u16, body: Vec<u8>, content_type: &str) -> Reply {
    let length = body.len();
    with_header(
        Response::new(StatusCode(status), vec![], Box::new(Cursor::new(body)) as Box<dyn Read + Send>, Some(length), None),
        "Content-Type",
        content_type,
    )
}

fn text(status: u16, body: &str, content_type: &str) -> Reply {
    bytes(status, body.as_bytes().to_vec(), content_type)
}

fn json_reply(status: u16, body: String) -> Reply {
    bytes(status, body.into_bytes(), "application/json")
}

fn error(status: u16, message: &str) -> Reply {
    json_reply(status, json!({ "message": message }).to_string())
}

fn empty(status: u16) -> Reply {
    bytes(status, Vec::new(), "text/plain")
}

fn with_header(reply: Reply, name: &str, value: &str) -> Reply {
    match Header::from_bytes(name.as_bytes(), value.as_bytes()) {
        Ok(header) => reply.with_header(header),
        Err(()) => reply,
    }
}

/// `--healthcheck`: asks the running reader on this machine for `/healthz`, for an image that has no curl. Exit code 0 when it answers 200.
pub fn healthcheck(options_listen: SocketAddr) -> i32 {
    use std::io::Write;
    use std::net::{IpAddr, Ipv4Addr, TcpStream};
    use std::time::Duration;

    let host = if options_listen.ip().is_unspecified() {
        IpAddr::V4(Ipv4Addr::LOCALHOST)
    } else {
        options_listen.ip()
    };
    let Ok(mut stream) = TcpStream::connect_timeout(&SocketAddr::new(host, options_listen.port()), Duration::from_secs(3)) else {
        return 1;
    };
    let _ = stream.set_read_timeout(Some(Duration::from_secs(3)));
    if stream.write_all(b"GET /healthz HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n").is_err() {
        return 1;
    }
    let mut answer = String::new();
    let _ = stream.take(64).read_to_string(&mut answer);
    i32::from(!answer.starts_with("HTTP/1.1 200"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn routes_are_the_fixed_list_and_nothing_else() {
        assert_eq!(route("/healthz"), Some(Route::Health));
        assert_eq!(route("/v1/docker/containers/json"), Some(Route::ContainerList));
        assert_eq!(route("/v1/docker/containers/web/json"), Some(Route::Json("web")));
        assert_eq!(route("/v1/docker/containers/web/stats"), Some(Route::Stats("web")));
        assert_eq!(route("/v1/docker/containers/web/logs"), Some(Route::Logs("web")));
        for path in [
            "/",
            "/v1",
            "/v1/docker/version",
            "/v1/docker/containers/create",
            "/v1/docker/containers/web/exec",
            "/v1/docker/containers/web/json/",
            "/v1/info/",
            "/V1/info",
            "/v1/docker/containers/a/b/json",
        ] {
            assert!(route(path).is_none(), "{path}");
        }
        assert_eq!(
            route("/v1/docker/containers//json"),
            Some(Route::Json("")),
            "an empty name is a route and a 400, not a lookup"
        );
    }

    #[test]
    fn query_values_are_read_by_key_and_booleans_are_strict() {
        assert_eq!(query_value("a=1&b=2&a=3", "a"), Some("1"));
        assert_eq!(query_value("flag", "flag"), Some(""));
        assert_eq!(query_value("", "a"), None);
        assert_eq!(optional_bool("all=TRUE", "all"), Some(Some(true)));
        assert_eq!(optional_bool("", "all"), Some(None));
        assert_eq!(optional_bool("all=yes", "all"), None);
    }

    #[test]
    fn log_parameters_have_defaults_are_clamped_and_refused_when_invalid() {
        assert_eq!(
            log_query("").unwrap(),
            LogQuery {
                stdout: true,
                stderr: true,
                timestamps: false,
                tail: 100,
                follow: false
            }
        );
        assert_eq!(
            log_query("tail=-1&follow=true&timestamps=true&stdout=false").unwrap(),
            LogQuery {
                stdout: false,
                stderr: true,
                timestamps: true,
                tail: 0,
                follow: true
            }
        );
        assert_eq!(log_query("tail=99999").unwrap().tail, MAX_LOG_LINES);
        assert!(log_query("tail=abc").is_err());
        assert!(log_query("follow=maybe").is_err());
    }
}
