use portarr_reader::config::{Options, ENV_PREFIX};
use portarr_reader::server;

fn main() {
    if std::env::args().nth(1).as_deref() == Some("--healthcheck") {
        // The health check needs no token: only where the reader listens.
        let listen = std::env::var(format!("{ENV_PREFIX}URLS")).ok().and_then(|urls| {
            urls.strip_prefix("http://")
                .map(|rest| rest.trim_end_matches('/').replacen("+:", "0.0.0.0:", 1).replacen("*:", "0.0.0.0:", 1))
        });
        let address = listen.as_deref().unwrap_or("0.0.0.0:8485").parse().unwrap_or_else(|_| ([0, 0, 0, 0], 8485).into());
        std::process::exit(server::healthcheck(address));
    }

    let options = match Options::from_env() {
        Ok(options) => options,
        Err(message) => {
            eprintln!("portarr-reader: {message}");
            std::process::exit(2);
        }
    };

    match server::start(options) {
        Ok(running) => {
            eprintln!("portarr-reader {} listening on http://{}", server::VERSION, running.address);
            running.wait();
        }
        Err(error) => {
            eprintln!("portarr-reader: cannot listen: {error}");
            std::process::exit(1);
        }
    }
}
