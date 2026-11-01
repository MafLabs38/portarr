//! The reader's only credential (D-100): a bearer token, compared by SHA-256 hash in constant time. It is never logged,
//! and a wrong one learns nothing.

use sha2::{Digest, Sha256};
use subtle::ConstantTimeEq;

pub struct Authenticator {
    expected: [u8; 32],
}

impl Authenticator {
    pub fn new(token: &str) -> Authenticator {
        Authenticator {
            expected: Sha256::digest(token.as_bytes()).into(),
        }
    }

    /// Whether the request carries the token, as `Authorization: Bearer <token>` (the scheme in any case, as HTTP says) or,
    /// when there is no bearer, as `X-Api-Key`. A wrong bearer is not rescued by the other header.
    pub fn accepts(&self, authorization: Option<&str>, api_key: Option<&str>) -> bool {
        match presented(authorization, api_key) {
            Some(token) if !token.is_empty() => {
                let hash: [u8; 32] = Sha256::digest(token.as_bytes()).into();
                hash.ct_eq(&self.expected).into()
            }
            _ => false,
        }
    }
}

fn presented<'a>(authorization: Option<&'a str>, api_key: Option<&'a str>) -> Option<&'a str> {
    if let Some(value) = authorization {
        let value = value.trim_start();
        if value.len() > 7 && value.is_char_boundary(7) && value[..7].eq_ignore_ascii_case("bearer ") {
            return Some(value[7..].trim());
        }
        if value.trim_end().eq_ignore_ascii_case("bearer") {
            return Some("");
        }
    }
    api_key.map(str::trim).filter(|key| !key.is_empty())
}

#[cfg(test)]
mod tests {
    use super::*;

    const TOKEN: &str = "reader-test-token-0123456789abcdef-0123456789";

    fn auth() -> Authenticator {
        Authenticator::new(TOKEN)
    }

    #[test]
    fn the_exact_token_is_accepted_as_a_bearer_or_an_api_key() {
        assert!(auth().accepts(Some(&format!("Bearer {TOKEN}")), None));
        assert!(auth().accepts(None, Some(TOKEN)));
    }

    #[test]
    fn the_scheme_is_case_insensitive() {
        assert!(auth().accepts(Some(&format!("bearer {TOKEN}")), None));
        assert!(auth().accepts(Some(&format!("BEARER {TOKEN}")), None));
    }

    #[test]
    fn anything_but_the_exact_token_is_refused() {
        let prefix = &TOKEN[..TOKEN.len() - 1];
        let extension = format!("{TOKEN}x");
        for authorization in [
            "Bearer ",
            "Bearer",
            "Basic reader-test-token-0123456789abcdef-0123456789",
            &format!("Bearer {prefix}"),
            &format!("Bearer {extension}"),
            "Bearer wrong",
            "",
        ] {
            assert!(!auth().accepts(Some(authorization), None), "{authorization:?}");
        }
        assert!(!auth().accepts(None, None));
        assert!(!auth().accepts(None, Some("")));
        assert!(!auth().accepts(None, Some("wrong")));
    }

    #[test]
    fn a_wrong_bearer_is_not_rescued_by_the_api_key_header() {
        assert!(!auth().accepts(Some("Bearer wrong"), Some(TOKEN)));
    }

    #[test]
    fn another_scheme_falls_back_to_the_api_key_header() {
        assert!(auth().accepts(Some("Basic abc"), Some(TOKEN)));
    }

    #[test]
    fn a_header_with_multibyte_characters_does_not_panic() {
        assert!(!auth().accepts(Some("Bearé token"), None));
        assert!(!auth().accepts(Some("日本語日本語日本語"), None));
    }
}
