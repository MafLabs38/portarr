//! The reader over real HTTP (D-100, D-102): behind one token, a window on a fake Docker (over TCP and over a Unix socket, the real
//! mode) that forwards only what Portarr reads, and no route for anything else.

use std::collections::HashMap;
use std::fs;
use std::io::{BufRead, BufReader, Read, Write};
use std::net::{SocketAddr, TcpListener, TcpStream};
use std::os::unix::net::UnixListener;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::mpsc::{channel, Receiver, Sender};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::Duration;

use portarr_reader::config::{DockerEndpoint, Options};
use portarr_reader::http_client::ChunkedReader;
use portarr_reader::server::{self, RunningServer};
use serde_json::{json, Value};

const TOKEN: &str = "reader-test-token-0123456789abcdef-0123456789";

fn fixtures() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("tests").join("fixtures")
}

// ---------------------------------------------------------------------------------------------------------------- fake Docker

enum Answer {
    Body {
        status: u16,
        body: String,
    },
    /// The same body, sent as two chunks (Docker does, for some answers).
    Chunked(String),
    /// A body sent as the test provides it, one chunk per message, ended when the sender is dropped.
    Stream(Mutex<Option<Receiver<Vec<u8>>>>),
}

struct FakeDocker {
    endpoint: DockerEndpoint,
    requests: Arc<Mutex<Vec<String>>>,
    answers: Arc<Mutex<HashMap<String, Arc<Answer>>>>,
    cleanup: Option<PathBuf>,
}

impl FakeDocker {
    fn tcp() -> FakeDocker {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        let fake = FakeDocker::new(DockerEndpoint::Tcp { host: "127.0.0.1".into(), port }, None);
        let (requests, answers) = (fake.requests.clone(), fake.answers.clone());
        thread::spawn(move || {
            for stream in listener.incoming().flatten() {
                let (requests, answers) = (requests.clone(), answers.clone());
                thread::spawn(move || serve(stream, &requests, &answers));
            }
        });
        fake
    }

    fn unix() -> FakeDocker {
        static COUNTER: AtomicUsize = AtomicUsize::new(0);
        let path = std::env::temp_dir().join(format!("portarr-reader-fake-{}-{}.sock", std::process::id(), COUNTER.fetch_add(1, Ordering::SeqCst)));
        let _ = fs::remove_file(&path);
        let listener = UnixListener::bind(&path).unwrap();
        let fake = FakeDocker::new(DockerEndpoint::Unix(path.to_str().unwrap().to_string()), Some(path));
        let (requests, answers) = (fake.requests.clone(), fake.answers.clone());
        thread::spawn(move || {
            for stream in listener.incoming().flatten() {
                let (requests, answers) = (requests.clone(), answers.clone());
                thread::spawn(move || serve(stream, &requests, &answers));
            }
        });
        fake
    }

    fn new(endpoint: DockerEndpoint, cleanup: Option<PathBuf>) -> FakeDocker {
        FakeDocker {
            endpoint,
            requests: Arc::default(),
            answers: Arc::default(),
            cleanup,
        }
    }

    fn answer(&self, path: &str, status: u16, body: &str) -> &FakeDocker {
        self.answers
            .lock()
            .unwrap()
            .insert(path.to_string(), Arc::new(Answer::Body { status, body: body.to_string() }));
        self
    }

    fn answer_chunked(&self, path: &str, body: &str) -> &FakeDocker {
        self.answers.lock().unwrap().insert(path.to_string(), Arc::new(Answer::Chunked(body.to_string())));
        self
    }

    fn stream(&self, path: &str) -> Sender<Vec<u8>> {
        let (sender, receiver) = channel();
        self.answers.lock().unwrap().insert(path.to_string(), Arc::new(Answer::Stream(Mutex::new(Some(receiver)))));
        sender
    }

    fn requests(&self) -> Vec<String> {
        self.requests.lock().unwrap().clone()
    }

    /// The query of the last request for `path`, as key/value pairs.
    fn query(&self, path: &str) -> HashMap<String, String> {
        let requests = self.requests();
        let target = requests
            .iter()
            .rev()
            .find(|target| target.split('?').next() == Some(path))
            .unwrap_or_else(|| panic!("Docker never received {path}: {requests:?}"));
        target
            .split_once('?')
            .map(|(_, query)| query)
            .unwrap_or_default()
            .split('&')
            .filter(|pair| !pair.is_empty())
            .map(|pair| {
                pair.split_once('=')
                    .map(|(k, v)| (k.to_string(), v.to_string()))
                    .unwrap_or((pair.to_string(), String::new()))
            })
            .collect()
    }
}

impl Drop for FakeDocker {
    fn drop(&mut self) {
        if let Some(path) = &self.cleanup {
            let _ = fs::remove_file(path);
        }
    }
}

fn serve(mut stream: impl Read + Write, requests: &Mutex<Vec<String>>, answers: &Mutex<HashMap<String, Arc<Answer>>>) {
    let mut reader = BufReader::new(&mut stream);
    let mut line = String::new();
    if reader.read_line(&mut line).unwrap_or(0) == 0 {
        return;
    }
    let method = line.split_whitespace().next().unwrap_or_default().to_string();
    let target = line.split_whitespace().nth(1).unwrap_or_default().to_string();
    loop {
        line.clear();
        if reader.read_line(&mut line).unwrap_or(0) == 0 || line.trim().is_empty() {
            break;
        }
    }
    requests.lock().unwrap().push(format!("{target}{}", if method == "GET" { "" } else { " (not a GET)" }));

    let path = target.split('?').next().unwrap_or_default();
    let answer = answers.lock().unwrap().get(path).cloned();
    let stream = reader.into_inner();
    match answer.as_deref() {
        Some(Answer::Body { status, body }) => {
            let _ = write!(
                stream,
                "HTTP/1.1 {status} X\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}",
                body.len()
            );
        }
        Some(Answer::Chunked(body)) => {
            let (first, second) = body.split_at(body.len() / 2);
            let _ = write!(
                stream,
                "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n{:x}\r\n{first}\r\n{:x}\r\n{second}\r\n0\r\n\r\n",
                first.len(),
                second.len()
            );
        }
        Some(Answer::Stream(receiver)) => {
            let Some(receiver) = receiver.lock().unwrap().take() else { return };
            let _ = write!(stream, "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n");
            let _ = stream.flush();
            while let Ok(chunk) = receiver.recv() {
                if write!(stream, "{:x}\r\n", chunk.len())
                    .and_then(|()| stream.write_all(&chunk))
                    .and_then(|()| stream.write_all(b"\r\n"))
                    .and_then(|()| stream.flush())
                    .is_err()
                {
                    return;
                }
            }
            let _ = write!(stream, "0\r\n\r\n");
        }
        None => {
            let body = r#"{"message":"No such container: gone"}"#;
            let _ = write!(stream, "HTTP/1.1 404 Not Found\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}", body.len());
        }
    }
}

// ---------------------------------------------------------------------------------------------------------------------- reader

struct Reader {
    server: Option<RunningServer>,
    addr: SocketAddr,
    proc_dir: PathBuf,
}

impl Reader {
    fn start(docker: DockerEndpoint) -> Reader {
        static COUNTER: AtomicUsize = AtomicUsize::new(0);
        let proc_dir = std::env::temp_dir().join(format!("portarr-reader-proc-{}-{}", std::process::id(), COUNTER.fetch_add(1, Ordering::SeqCst)));
        copy_proc(&fixtures().join("proc-1"), &proc_dir);
        let options = Options {
            token: TOKEN.to_string(),
            docker,
            listen: "127.0.0.1:0".parse().unwrap(),
            proc_path: proc_dir.to_str().unwrap().to_string(),
            sys_path: fixtures().join("sys").to_str().unwrap().to_string(),
            host_name: Some("test-host".to_string()),
        };
        let server = server::start(options).unwrap();
        let addr = server.address;
        Reader {
            server: Some(server),
            addr,
            proc_dir,
        }
    }

    fn with(fake: &FakeDocker) -> Reader {
        Reader::start(fake.endpoint.clone())
    }

    /// An authorized GET.
    fn get(&self, path: &str) -> Response {
        request(self.addr, "GET", path, &[("Authorization", &format!("Bearer {TOKEN}"))])
    }
}

impl Drop for Reader {
    fn drop(&mut self) {
        if let Some(server) = self.server.take() {
            server.stop();
        }
        let _ = fs::remove_dir_all(&self.proc_dir);
    }
}

fn copy_proc(from: &Path, to: &Path) {
    let _ = fs::remove_dir_all(to);
    fs::create_dir_all(to).unwrap();
    for entry in fs::read_dir(from).unwrap().flatten() {
        fs::copy(entry.path(), to.join(entry.file_name())).unwrap();
    }
}

struct Response {
    status: u16,
    headers: Vec<(String, String)>,
    body: Vec<u8>,
}

impl Response {
    fn text(&self) -> String {
        String::from_utf8_lossy(&self.body).into_owned()
    }

    fn json(&self) -> Value {
        serde_json::from_slice(&self.body).unwrap_or_else(|_| panic!("not JSON: {}", self.text()))
    }

    fn header(&self, name: &str) -> Option<&str> {
        self.headers.iter().find(|(key, _)| key.eq_ignore_ascii_case(name)).map(|(_, value)| value.as_str())
    }
}

fn request(addr: SocketAddr, method: &str, path: &str, headers: &[(&str, &str)]) -> Response {
    let mut stream = TcpStream::connect(addr).unwrap();
    stream.set_read_timeout(Some(Duration::from_secs(10))).unwrap();
    let extra: String = headers.iter().map(|(name, value)| format!("{name}: {value}\r\n")).collect();
    write!(stream, "{method} {path} HTTP/1.1\r\nHost: reader\r\n{extra}Content-Length: 0\r\nConnection: close\r\n\r\n").unwrap();
    let mut reader = BufReader::new(stream);
    let (status, headers) = read_head(&mut reader);
    let mut body = Vec::new();
    if headers
        .iter()
        .any(|(name, value)| name.eq_ignore_ascii_case("transfer-encoding") && value.contains("chunked"))
    {
        ChunkedReader::new(reader).read_to_end(&mut body).unwrap();
    } else {
        reader.read_to_end(&mut body).unwrap();
    }
    Response { status, headers, body }
}

fn read_head(reader: &mut impl BufRead) -> (u16, Vec<(String, String)>) {
    let mut line = String::new();
    reader.read_line(&mut line).unwrap();
    let status = line.split_whitespace().nth(1).unwrap().parse().unwrap();
    let mut headers = Vec::new();
    loop {
        line.clear();
        reader.read_line(&mut line).unwrap();
        if line.trim().is_empty() {
            return (status, headers);
        }
        let (name, value) = line.split_once(':').unwrap();
        headers.push((name.trim().to_string(), value.trim().to_string()));
    }
}

fn assert_golden(name: &str, actual: &str) {
    let path = fixtures().join(name);
    if std::env::var("UPDATE_GOLDEN").is_ok() {
        fs::write(&path, actual).unwrap();
        // A regeneration run is never a passing run: the files are reviewed in the diff and the .NET test reads them.
        panic!("{name} was regenerated: run the tests again without UPDATE_GOLDEN");
    }
    assert_eq!(
        actual,
        fs::read_to_string(&path).unwrap_or_else(|_| panic!("{name} is missing: run with UPDATE_GOLDEN=1")),
        "{name} differs: run with UPDATE_GOLDEN=1 if the change is intended, and check the .NET host-metrics test still reads it"
    );
}

const CONTAINERS: &str = r#"[{"Id":"abc123","Names":["/web"],"Image":"nginx","State":"running","Status":"Up 3 hours","Created":1700000000,"Labels":{"com.docker.compose.project":"shop"},"Ports":[],"Mounts":[],"Command":"nginx -g secret-arg","HostConfig":{"NetworkMode":"x"},"NetworkSettings":{"Networks":{}}}]"#;
const STATS: &str = r#"{"cpu_stats":{},"memory_stats":{"usage":1}}"#;

fn docker_with_everything(fake: &FakeDocker) {
    fake.answer("/info", 200, r#"{"NCPU":8,"MemTotal":16000,"Name":"secret-host","SecurityOptions":["x"]}"#)
        .answer("/containers/json", 200, CONTAINERS)
        .answer(
            "/containers/web/json",
            200,
            r#"{"Id":"abc","Config":{"Tty":true,"Env":["DB_PASSWORD=hunter2"]},"HostConfig":{}}"#,
        )
        .answer("/containers/web/stats", 200, STATS)
        .answer("/containers/web/logs", 200, "frame-bytes")
        .answer("/containers/broken/json", 500, "secret stack trace of the engine");
}

// ------------------------------------------------------------------------------------------------------------------------ tests

#[test]
fn everything_but_the_health_check_is_behind_the_token() {
    let reader = Reader::with(&FakeDocker::tcp());

    assert_eq!(request(reader.addr, "GET", "/healthz", &[]).text(), "ok");
    let refused = request(reader.addr, "GET", "/v1/info", &[]);
    assert_eq!(refused.status, 401);
    assert_eq!(refused.header("WWW-Authenticate"), Some("Bearer realm=\"portarr-reader\""));
    assert_eq!(
        request(reader.addr, "GET", "/nothing/here", &[]).status,
        401,
        "an anonymous caller learns nothing about the routes"
    );
    assert_eq!(request(reader.addr, "GET", "/v1/host", &[("Authorization", "Bearer wrong")]).status, 401);
}

#[test]
fn the_accepted_credentials_are_a_bearer_token_or_an_api_key() {
    let reader = Reader::with(&FakeDocker::tcp());
    let bearer = |value: &str| request(reader.addr, "GET", "/v1/info", &[("Authorization", value)]);

    let info = bearer(&format!("Bearer {TOKEN}"));
    assert_eq!(info.status, 200);
    assert_eq!(
        info.json(),
        json!({ "name": "portarr-reader", "version": server::VERSION, "host": "test-host", "capabilities": ["host", "docker"] })
    );
    assert!(!info.text().contains(TOKEN));
    assert_eq!(request(reader.addr, "GET", "/v1/info", &[("X-Api-Key", TOKEN)]).status, 200);
    assert_eq!(bearer(&format!("bearer {TOKEN}")).status, 200, "an HTTP authentication scheme is case-insensitive");
}

#[test]
fn a_malformed_or_wrong_credential_is_refused() {
    let reader = Reader::with(&FakeDocker::tcp());
    let bearer = |value: &str| request(reader.addr, "GET", "/v1/info", &[("Authorization", value)]);

    for value in [
        "Bearer ",
        "Bearer",
        &format!("Basic {TOKEN}"),
        &format!("Bearer {TOKEN}x"),
        &format!("Bearer {}", &TOKEN[1..]),
    ] {
        assert_eq!(bearer(value).status, 401, "{value}");
    }
}

#[test]
fn a_wrong_bearer_is_not_rescued_by_an_api_key() {
    let reader = Reader::with(&FakeDocker::tcp());

    let both = request(reader.addr, "GET", "/v1/info", &[("Authorization", "Bearer wrong"), ("X-Api-Key", TOKEN)]);
    assert_eq!(both.status, 401);
}

#[test]
fn two_authorization_headers_are_never_both_trusted() {
    let reader = Reader::with(&FakeDocker::tcp());

    let doubled = request(
        reader.addr,
        "GET",
        "/v1/info",
        &[("Authorization", "Bearer wrong"), ("Authorization", &format!("Bearer {TOKEN}"))],
    );
    assert_eq!(doubled.status, 401, "the first header decides");
}

#[test]
fn head_is_not_a_route_either() {
    let reader = Reader::with(&FakeDocker::tcp());

    let head = request(reader.addr, "HEAD", "/v1/host", &[("Authorization", &format!("Bearer {TOKEN}"))]);
    assert_eq!(head.status, 405);
    assert_eq!(head.header("Allow"), Some("GET"));
}

#[test]
fn the_host_is_served_in_the_node_exporter_format_and_matches_what_portarrs_sampler_reads() {
    let reader = Reader::with(&FakeDocker::tcp());

    let first = reader.get("/v1/host");
    assert_eq!(first.status, 200);
    assert!(first.header("Content-Type").unwrap().starts_with("text/plain; version=0.0.4"));
    assert_golden("golden-host-1.txt", &first.text());

    // One second later cpu0 spent 50 of 100 ticks idle and cpu1 all of its 100: a quarter of the machine was busy.
    copy_proc(&fixtures().join("proc-2"), &reader.proc_dir);
    assert_golden("golden-host-2.txt", &reader.get("/v1/host").text());
    assert!(first.text().contains("node_hwmon_temp_celsius{chip=\"coretemp\",sensor=\"temp1\"} 45\n"));
    assert!(!first.text().contains("nvme"), "only CPU chips are read, not the disk's");
}

#[test]
fn an_unreadable_proc_is_a_bad_gateway_that_names_no_path() {
    let reader = Reader::with(&FakeDocker::tcp());
    fs::remove_file(reader.proc_dir.join("meminfo")).unwrap();

    let response = reader.get("/v1/host");

    assert_eq!(response.status, 502);
    assert!(response.text().contains("could not be read") && !response.text().contains(reader.proc_dir.to_str().unwrap()));
}

fn forwards_only_what_portarr_reads(fake: &FakeDocker) {
    docker_with_everything(fake);
    fake.answer("/containers/abc123/stats", 200, STATS);
    let reader = Reader::with(fake);

    let info = reader.get("/v1/docker/info").json();
    let list = reader.get("/v1/docker/containers/json?all=true").json();
    let inspect = reader.get("/v1/docker/containers/web/json");
    let stats = reader.get("/v1/docker/containers/web/stats?stream=true");
    let one_shot = reader.get("/v1/docker/containers/abc123/stats?one-shot=true");
    let logs = reader.get("/v1/docker/containers/web/logs?tail=99999&follow=false&timestamps=true");

    assert_eq!(info, json!({ "NCPU": 8, "MemTotal": 16000 }));
    let listed = list[0].as_object().unwrap();
    assert_eq!(
        listed.keys().map(String::as_str).collect::<Vec<_>>(),
        ["Created", "Id", "Image", "Labels", "Mounts", "Names", "Ports", "State", "Status"],
        "Command, HostConfig and NetworkSettings are not forwarded"
    );
    assert_eq!(listed["Labels"], json!({ "com.docker.compose.project": "shop" }));
    assert_eq!(
        inspect.text(),
        r#"{"Config":{"Tty":true},"Id":"abc"}"#,
        "the inspect holds the container's environment: only the id and the TTY flag leave"
    );
    assert_eq!((stats.status, stats.text().as_str()), (200, STATS));
    assert_eq!(one_shot.status, 200);
    assert_eq!((logs.status, logs.text().as_str()), (200, "frame-bytes"));
    assert_eq!(logs.header("Content-Type"), Some("application/octet-stream"));

    assert_eq!(fake.query("/containers/json")["all"], "true");
    assert_eq!(
        fake.query("/containers/web/stats").get("stream").map(String::as_str),
        Some("false"),
        "usage is always one sample, whatever the caller asks"
    );
    assert!(!fake.query("/containers/web/stats").contains_key("one-shot"), "it is passed on only when asked for");
    assert_eq!(fake.query("/containers/abc123/stats").get("one-shot").map(String::as_str), Some("true"));
    let log_query = fake.query("/containers/web/logs");
    assert_eq!(
        ["stdout", "stderr", "timestamps", "tail", "follow"].map(|key| log_query[key].as_str()),
        ["true", "true", "true", "5000", "false"],
        "the line count is capped"
    );
    assert!(fake.requests().iter().all(|target| !target.contains("not a GET")), "the reader never writes to Docker");
}

#[test]
fn docker_over_tcp_is_forwarded_only_as_far_as_portarr_reads_it() {
    forwards_only_what_portarr_reads(&FakeDocker::tcp());
}

#[test]
fn docker_over_a_unix_socket_the_real_mode_is_forwarded_the_same() {
    forwards_only_what_portarr_reads(&FakeDocker::unix());
}

#[test]
fn a_chunked_answer_from_docker_is_decoded() {
    let fake = FakeDocker::tcp();
    fake.answer_chunked("/containers/json", CONTAINERS);
    let reader = Reader::with(&fake);

    let list = reader.get("/v1/docker/containers/json");

    assert_eq!(list.status, 200);
    assert_eq!(list.json()[0]["Names"], json!(["/web"]));
}

#[test]
fn anything_else_has_no_route_and_never_reaches_docker() {
    let fake = FakeDocker::tcp();
    docker_with_everything(&fake);
    let reader = Reader::with(&fake);
    let post = |path: &str| request(reader.addr, "POST", path, &[("Authorization", &format!("Bearer {TOKEN}"))]);

    assert_eq!(post("/v1/docker/containers/json").status, 405);
    assert_eq!(post("/v1/docker/containers/web/json").status, 405);
    assert_eq!(post("/v1/host").status, 405);
    assert_eq!(post("/v1/docker/containers/web/json").header("Allow"), Some("GET"));
    assert_eq!(
        post("/v1/docker/containers/create").status,
        404,
        "there is no route to create anything, the verb is not even looked at"
    );
    for path in [
        "/v1/docker/containers/web/exec",
        "/v1/docker/version",
        "/v1/docker/containers/web/json/",
        "/v1/docker/containers/web/archive",
        "/v1/docker/networks",
        "/",
    ] {
        assert_eq!(reader.get(path).status, 404, "{path}");
    }
    assert!(fake.requests().is_empty(), "{:?}", fake.requests());
}

#[test]
fn a_parameter_that_is_not_valid_is_refused_without_asking_docker() {
    let fake = FakeDocker::tcp();
    docker_with_everything(&fake);
    let reader = Reader::with(&fake);

    for path in [
        "/v1/docker/containers/web/logs?tail=abc",
        "/v1/docker/containers/web/logs?tail=-1",
        "/v1/docker/containers/web/logs?follow=maybe",
        "/v1/docker/containers/web/logs?stdout=2",
        "/v1/docker/containers/json?all=yes",
    ] {
        assert_eq!(reader.get(path).status, 400, "{path}");
    }
    assert!(fake.requests().is_empty());
}

#[test]
fn an_odd_container_name_never_reaches_docker() {
    let fake = FakeDocker::tcp();
    docker_with_everything(&fake);
    let reader = Reader::with(&fake);
    let long = "a".repeat(300);

    for name in ["..", "%2e%2e", "a%2Fb", "-lead", "_lead", "name%20with%20space", "x;y", "we%62", long.as_str()] {
        for action in ["json", "stats", "logs"] {
            let status = reader.get(&format!("/v1/docker/containers/{name}/{action}")).status;
            assert!(status == 400 || status == 404, "{name}/{action}: {status}");
        }
    }
    assert!(fake.requests().is_empty(), "{:?}", fake.requests());
}

#[test]
fn dockers_not_found_stays_a_404_and_anything_else_it_says_is_hidden() {
    let fake = FakeDocker::tcp();
    docker_with_everything(&fake);
    let reader = Reader::with(&fake);

    let gone = reader.get("/v1/docker/containers/gone/json");
    let broken = reader.get("/v1/docker/containers/broken/json");
    let down = Reader::start(DockerEndpoint::Tcp {
        host: "127.0.0.1".into(),
        port: 1,
    })
    .get("/v1/docker/containers/json");
    let missing_socket = Reader::start(DockerEndpoint::Unix("/nonexistent/docker.sock".into())).get("/v1/docker/info");

    assert_eq!(gone.status, 404);
    assert!(!gone.text().contains("No such container"), "Docker's own text is not forwarded");
    assert_eq!(broken.status, 502);
    assert!(!broken.text().contains("stack trace"));
    assert_eq!((down.status, down.json()["message"].as_str()), (502, Some("Docker is not reachable.")));
    assert_eq!(missing_socket.status, 502);
}

#[test]
fn a_followed_log_is_live_each_line_arrives_before_the_next_is_written() {
    let fake = FakeDocker::tcp();
    let lines = fake.stream("/containers/web/logs");
    let reader = Reader::with(&fake);

    let mut stream = TcpStream::connect(reader.addr).unwrap();
    stream.set_read_timeout(Some(Duration::from_secs(5))).unwrap();
    write!(
        stream,
        "GET /v1/docker/containers/web/logs?follow=true HTTP/1.1\r\nHost: r\r\nAuthorization: Bearer {TOKEN}\r\nConnection: close\r\n\r\n"
    )
    .unwrap();
    let mut reader_stream = BufReader::new(stream);
    let (status, headers) = read_head(&mut reader_stream);
    assert_eq!(status, 200);
    assert!(headers
        .iter()
        .any(|(name, value)| name.eq_ignore_ascii_case("transfer-encoding") && value.contains("chunked")));
    let mut body = ChunkedReader::new(reader_stream);

    lines.send(b"first line\n".to_vec()).unwrap();
    let mut buffer = [0u8; 64];
    let read = body.read(&mut buffer).expect("the first line must arrive while the log is still open, not when it ends");
    assert_eq!(&buffer[..read], b"first line\n");

    lines.send(b"second line\n".to_vec()).unwrap();
    let read = body.read(&mut buffer).unwrap();
    assert_eq!(&buffer[..read], b"second line\n");

    drop(lines);
    assert_eq!(body.read(&mut buffer).unwrap(), 0, "the log ends when the container stops writing");
}

#[test]
fn the_health_check_command_answers_for_a_running_reader_and_fails_for_none() {
    let reader = Reader::with(&FakeDocker::tcp());

    assert_eq!(server::healthcheck(reader.addr), 0);
    assert_eq!(server::healthcheck("127.0.0.1:1".parse().unwrap()), 1);
}
