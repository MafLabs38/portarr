//! portarr-reader (D-100, D-102): the optional, read-only reader of one machine. One bearer token, host metrics in the node_exporter
//! text format at `/v1/host`, a filtered read-only window on the Docker Engine API at `/v1/docker/`, and `/healthz`.

pub mod auth;
pub mod config;
pub mod docker;
pub mod host;
pub mod http_client;
pub mod server;
