//! A filtered, read-only window on the Docker Engine API (D-100, D-102). It is not a proxy: each function asks Docker for one fixed GET
//! path with the few parameters it accepts and returns only what Portarr reads. The container list loses what it carries beyond
//! those fields (a command line often holds a secret); the inspect is reduced to the id and the TTY flag, because the rest holds
//! the container's environment variables.

use std::io::ErrorKind;

use serde_json::{json, Map, Value};

use crate::config::DockerEndpoint;
use crate::http_client::{self, Upstream, CALL_TIMEOUT};

/// Most log lines one request may ask for, like Portarr's own limit.
pub const MAX_LOG_LINES: i64 = 5000;

/// The fields of a listed container that Portarr's Docker connector reads (the others are not forwarded).
const LISTED_FIELDS: [&str; 9] = ["Id", "Names", "Image", "State", "Status", "Created", "Labels", "Ports", "Mounts"];

#[derive(Debug, PartialEq, Eq)]
pub enum GatewayError {
    /// Docker says there is no such container.
    NotFound,
    /// Docker refused the request (anything else than success or not found).
    Refused,
    Unreachable,
    Timeout,
    /// Docker answered something that is not what it should.
    Unexpected,
}

/// The only parameters of a logs request: which streams, timestamps, how many lines, and whether to follow.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct LogQuery {
    pub stdout: bool,
    pub stderr: bool,
    pub timestamps: bool,
    pub tail: i64,
    pub follow: bool,
}

/// Container names go into URL paths: only Docker's own name characters are accepted.
pub fn is_valid_name(name: &str) -> bool {
    let mut characters = name.chars();
    name.len() <= 255 && characters.next().is_some_and(|first| first.is_ascii_alphanumeric()) && characters.all(|c| c.is_ascii_alphanumeric() || matches!(c, '_' | '.' | '-'))
}

pub struct Docker {
    endpoint: DockerEndpoint,
}

impl Docker {
    pub fn new(endpoint: DockerEndpoint) -> Docker {
        Docker { endpoint }
    }

    /// The host's capacity: the CPU count and the memory, nothing else of `/info`.
    pub fn info(&self) -> Result<String, GatewayError> {
        let info = self.json("/info")?;
        Ok(json!({ "NCPU": number_or_null(info.get("NCPU")), "MemTotal": number_or_null(info.get("MemTotal")) }).to_string())
    }

    pub fn list(&self, all: bool) -> Result<String, GatewayError> {
        let Value::Array(containers) = self.json(&format!("/containers/json?all={all}"))? else {
            return Err(GatewayError::Unexpected);
        };
        let kept: Vec<Value> = containers
            .iter()
            .map(|container| {
                Value::Object(
                    LISTED_FIELDS
                        .iter()
                        .filter_map(|field| container.get(*field).map(|value| (field.to_string(), value.clone())))
                        .collect::<Map<_, _>>(),
                )
            })
            .collect();
        Ok(Value::Array(kept).to_string())
    }

    /// The container's id and whether it has a TTY (the log stream is framed differently): the rest of the inspect holds its environment.
    pub fn inspect(&self, name: &str) -> Result<String, GatewayError> {
        let inspect = self.json(&format!("/containers/{name}/json"))?;
        let tty = inspect.pointer("/Config/Tty").and_then(Value::as_bool).unwrap_or(false);
        Ok(json!({ "Id": inspect.get("Id").filter(|id| id.is_string()).cloned().unwrap_or(Value::Null), "Config": { "Tty": tty } }).to_string())
    }

    /// One usage sample, never a stream. `one_shot` is passed on as asked: Portarr's infrastructure loop asks for Docker's one-shot sample
    /// (no wait for a second reading) every few seconds and measures the CPU itself.
    pub fn stats(&self, name: &str, one_shot: bool) -> Result<Vec<u8>, GatewayError> {
        let one_shot = if one_shot { "&one-shot=true" } else { "" };
        self.call(&format!("/containers/{name}/stats?stream=false{one_shot}"), Some(CALL_TIMEOUT))?
            .read_all()
            .map_err(read_error)
    }

    /// The container's logs as Docker frames them, as a stream: it ends when the container stops writing or the caller goes away.
    pub fn logs(&self, name: &str, query: LogQuery) -> Result<Upstream, GatewayError> {
        let target = format!(
            "/containers/{name}/logs?stdout={}&stderr={}&timestamps={}&tail={}&follow={}",
            query.stdout,
            query.stderr,
            query.timestamps,
            query.tail.clamp(0, MAX_LOG_LINES),
            query.follow
        );
        self.call(&target, if query.follow { None } else { Some(CALL_TIMEOUT) })
    }

    fn json(&self, target: &str) -> Result<Value, GatewayError> {
        let bytes = self.call(target, Some(CALL_TIMEOUT))?.read_all().map_err(read_error)?;
        serde_json::from_slice(&bytes).map_err(|_| GatewayError::Unexpected)
    }

    fn call(&self, target: &str, read_timeout: Option<std::time::Duration>) -> Result<Upstream, GatewayError> {
        let upstream = http_client::get(&self.endpoint, target, read_timeout).map_err(read_error)?;
        match upstream.status {
            200..=299 => Ok(upstream),
            404 => Err(GatewayError::NotFound),
            _ => Err(GatewayError::Refused),
        }
    }
}

fn number_or_null(value: Option<&Value>) -> Value {
    value.filter(|value| value.is_number()).cloned().unwrap_or(Value::Null)
}

fn read_error(error: std::io::Error) -> GatewayError {
    match error.kind() {
        ErrorKind::TimedOut | ErrorKind::WouldBlock => GatewayError::Timeout,
        ErrorKind::InvalidData | ErrorKind::UnexpectedEof => GatewayError::Unexpected,
        _ => GatewayError::Unreachable,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_docker_container_names_are_accepted() {
        for name in ["web", "a", "web-1_x.y", "0abc", &"a".repeat(255)] {
            assert!(is_valid_name(name), "{name}");
        }
        for name in ["", "..", "%2e%2e", "a%2Fb", "-lead", "_lead", ".lead", "a b", "a/b", "a\nb", "日本", &"a".repeat(256)] {
            assert!(!is_valid_name(name), "{name:?}");
        }
    }

    #[test]
    fn io_errors_map_to_the_reasons_the_reader_gives() {
        for (kind, expected) in [
            (ErrorKind::TimedOut, GatewayError::Timeout),
            (ErrorKind::WouldBlock, GatewayError::Timeout),
            (ErrorKind::ConnectionRefused, GatewayError::Unreachable),
            (ErrorKind::NotFound, GatewayError::Unreachable),
            (ErrorKind::InvalidData, GatewayError::Unexpected),
            (ErrorKind::UnexpectedEof, GatewayError::Unexpected),
        ] {
            assert_eq!(read_error(std::io::Error::from(kind)), expected, "{kind:?}");
        }
    }
}
