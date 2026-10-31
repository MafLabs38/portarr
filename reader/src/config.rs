//! The reader's settings, from `PORTARR_READER_*` environment variables (D-100, D-102). Only the token is required.

use std::net::SocketAddr;

pub const ENV_PREFIX: &str = "PORTARR_READER_";

/// Shortest accepted token, in characters: it is the only thing between the network and a window on Docker.
pub const MIN_TOKEN_LENGTH: usize = 32;

/// Where the Docker Engine API is: a Unix socket, or a read-only socket proxy over plain HTTP.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DockerEndpoint {
    Unix(String),
    Tcp { host: String, port: u16 },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Options {
    pub token: String,
    pub docker: DockerEndpoint,
    pub listen: SocketAddr,
    pub proc_path: String,
    pub sys_path: String,
    pub host_name: Option<String>,
}

impl Options {
    pub fn from_env() -> Result<Options, String> {
        Options::from_lookup(|key| std::env::var(format!("{ENV_PREFIX}{key}")).ok())
    }

    /// Reads the settings through `get`, which maps a key (`TOKEN`, `DOCKER`, `URLS`, `PROCPATH`, `SYSPATH`, `HOSTNAME`)
    /// to its value. The error names the setting, never its value.
    pub fn from_lookup(get: impl Fn(&str) -> Option<String>) -> Result<Options, String> {
        let token = get("TOKEN").unwrap_or_default();
        if token.chars().count() < MIN_TOKEN_LENGTH {
            return Err(format!(
                "{ENV_PREFIX}TOKEN must be set to at least {MIN_TOKEN_LENGTH} characters (for example the output of: openssl rand -hex 32)."
            ));
        }

        let docker = parse_docker(&get("DOCKER").unwrap_or_else(|| "unix:///var/run/docker.sock".to_string()))?;
        let listen = parse_listen(&get("URLS").unwrap_or_else(|| "http://0.0.0.0:8485".to_string()))?;

        Ok(Options {
            token,
            docker,
            listen,
            proc_path: get("PROCPATH").unwrap_or_else(|| "/proc".to_string()),
            sys_path: get("SYSPATH").unwrap_or_else(|| "/sys".to_string()),
            host_name: get("HOSTNAME").filter(|name| !name.trim().is_empty()),
        })
    }
}

fn parse_docker(value: &str) -> Result<DockerEndpoint, String> {
    let invalid = || format!("{ENV_PREFIX}DOCKER must be unix:///path/to/docker.sock or the http:// URL of a read-only Docker socket proxy, without credentials.");

    if let Some(path) = value.strip_prefix("unix://") {
        return if path.starts_with('/') && path.len() > 1 {
            Ok(DockerEndpoint::Unix(path.to_string()))
        } else {
            Err(invalid())
        };
    }

    let (rest, default_port) = if let Some(rest) = value.strip_prefix("http://") {
        (rest, 80)
    } else if let Some(rest) = value.strip_prefix("tcp://") {
        (rest, 2375)
    } else {
        return Err(invalid());
    };

    let authority = rest.split(['/', '?', '#']).next().unwrap_or_default();
    if authority.is_empty() || authority.contains('@') {
        return Err(invalid());
    }

    match authority.rsplit_once(':') {
        Some((host, port)) if !host.is_empty() => port.parse::<u16>().map(|port| DockerEndpoint::Tcp { host: host.to_string(), port }).map_err(|_| invalid()),
        Some(_) => Err(invalid()),
        None => Ok(DockerEndpoint::Tcp {
            host: authority.to_string(),
            port: default_port,
        }),
    }
}

/// `http://+:8485` and `http://*:8485` (the ASP.NET spelling the documentation used) mean every interface.
fn parse_listen(value: &str) -> Result<SocketAddr, String> {
    let invalid = || format!("{ENV_PREFIX}URLS must be an http://address:port to listen on (for example http://0.0.0.0:8485).");
    let authority = value.strip_prefix("http://").ok_or_else(invalid)?.trim_end_matches('/');
    let authority = authority.replacen("+:", "0.0.0.0:", 1).replacen("*:", "0.0.0.0:", 1);
    authority.parse::<SocketAddr>().map_err(|_| invalid())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashMap;

    const TOKEN: &str = "0123456789abcdef0123456789abcdef";

    fn options(pairs: &[(&str, &str)]) -> Result<Options, String> {
        let map: HashMap<String, String> = pairs.iter().map(|(k, v)| (k.to_string(), v.to_string())).collect();
        Options::from_lookup(|key| map.get(key).cloned())
    }

    #[test]
    fn defaults_apply_when_only_the_token_is_set() {
        let options = options(&[("TOKEN", TOKEN)]).unwrap();

        assert_eq!(options.docker, DockerEndpoint::Unix("/var/run/docker.sock".to_string()));
        assert_eq!(options.listen, "0.0.0.0:8485".parse().unwrap());
        assert_eq!((options.proc_path.as_str(), options.sys_path.as_str()), ("/proc", "/sys"));
        assert_eq!(options.host_name, None);
    }

    #[test]
    fn a_token_that_is_missing_or_short_stops_the_start_and_is_never_echoed() {
        for token in ["", "too-short", "0123456789abcdef0123456789abcde"] {
            let error = options(&[("TOKEN", token)]).unwrap_err();

            assert!(error.contains("PORTARR_READER_TOKEN") && error.contains("at least 32"), "{error}");
            assert!(token.is_empty() || !error.contains(token));
        }
        assert!(options(&[]).is_err());
    }

    #[test]
    fn docker_endpoints_are_a_socket_or_a_plain_http_proxy() {
        let tcp = |host: &str, port: u16| DockerEndpoint::Tcp { host: host.to_string(), port };
        for (value, expected) in [
            ("unix:///run/docker.sock", DockerEndpoint::Unix("/run/docker.sock".to_string())),
            ("http://socket-proxy:2375", tcp("socket-proxy", 2375)),
            ("http://socket-proxy", tcp("socket-proxy", 80)),
            ("tcp://10.0.0.10", tcp("10.0.0.10", 2375)),
            ("http://10.0.0.10:2376/", tcp("10.0.0.10", 2376)),
        ] {
            assert_eq!(options(&[("TOKEN", TOKEN), ("DOCKER", value)]).unwrap().docker, expected, "{value}");
        }
    }

    #[test]
    fn docker_endpoints_that_are_not_understood_are_refused_by_name() {
        for value in [
            "ftp://docker.example",
            "https://docker.example",
            "http://user:pw@docker.example:2375",
            "not a url",
            "unix://",
            "unix://relative",
            "http://",
            "http://:2375",
            "http://host:notaport",
        ] {
            let error = options(&[("TOKEN", TOKEN), ("DOCKER", value)]).unwrap_err();

            assert!(error.contains("PORTARR_READER_DOCKER"), "{value}: {error}");
        }
    }

    #[test]
    fn the_listen_address_accepts_the_aspnet_spelling_of_every_interface() {
        for (value, expected) in [
            ("http://+:8485", "0.0.0.0:8485"),
            ("http://*:9000", "0.0.0.0:9000"),
            ("http://127.0.0.1:8495/", "127.0.0.1:8495"),
        ] {
            assert_eq!(options(&[("TOKEN", TOKEN), ("URLS", value)]).unwrap().listen, expected.parse().unwrap(), "{value}");
        }
        assert!(options(&[("TOKEN", TOKEN), ("URLS", "ftp://x:1")]).is_err());
        assert!(options(&[("TOKEN", TOKEN), ("URLS", "http://nope")]).is_err());
    }
}
