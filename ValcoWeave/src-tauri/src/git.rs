//! Git + GitHub support: runs the user's own `git`, keeps the GitHub token in the
//! OS credential store (Windows Credential Manager / macOS Keychain / Secret Service),
//! and talks to GitHub for sign-in (device flow) and creating repositories.

use base64::Engine;
use serde::{Deserialize, Serialize};
use std::process::Command;

type CmdResult<T> = Result<T, String>;

fn err<E: std::fmt::Display>(e: E) -> String {
    e.to_string()
}

const KEYRING_SERVICE: &str = "WeaveCraft";
const KEYRING_USER: &str = "github-token";
const UA: &str = "Valco Weave (https://github.com/Raidenplays/valco-weave)";

/* ------------------------------------------------------------------- git */

#[derive(Serialize)]
pub struct GitOutput {
    code: i32,
    stdout: String,
    stderr: String,
}

fn git_command(cwd: &str) -> Command {
    let mut c = Command::new("git");
    c.current_dir(cwd);
    c.env("GIT_TERMINAL_PROMPT", "0"); // never hang waiting for a password prompt
    c.env("LC_ALL", "C");
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        c.creation_flags(0x0800_0000); // CREATE_NO_WINDOW: no console flashes
    }
    c
}

/// Runs `git <args>` in `cwd`. When `auth` is set, the stored GitHub token is
/// passed as an HTTP header for that one command (it is never written to .git/config).
#[tauri::command]
pub async fn git_exec(cwd: String, args: Vec<String>, auth: Option<bool>) -> CmdResult<GitOutput> {
    tauri::async_runtime::spawn_blocking(move || {
        let mut c = git_command(&cwd);
        if auth.unwrap_or(false) {
            if let Ok(token) = token_get_inner() {
                let basic = base64::engine::general_purpose::STANDARD.encode(format!("x-access-token:{token}"));
                c.arg("-c").arg(format!("http.https://github.com/.extraheader=AUTHORIZATION: basic {basic}"));
            }
        }
        c.args(&args);
        let out = c.output().map_err(|e| {
            if e.kind() == std::io::ErrorKind::NotFound {
                "GIT_NOT_FOUND".to_string()
            } else {
                e.to_string()
            }
        })?;
        Ok(GitOutput {
            code: out.status.code().unwrap_or(-1),
            stdout: String::from_utf8_lossy(&out.stdout).into_owned(),
            stderr: String::from_utf8_lossy(&out.stderr).into_owned(),
        })
    })
    .await
    .map_err(err)?
}

/* --------------------------------------------------------- token storage */

fn entry() -> CmdResult<keyring::Entry> {
    keyring::Entry::new(KEYRING_SERVICE, KEYRING_USER).map_err(err)
}

fn token_get_inner() -> CmdResult<String> {
    entry()?.get_password().map_err(err)
}

#[tauri::command]
pub fn gh_token_get() -> Option<String> {
    token_get_inner().ok()
}

#[tauri::command]
pub fn gh_token_set(token: String) -> CmdResult<()> {
    entry()?.set_password(token.trim()).map_err(err)
}

#[tauri::command]
pub fn gh_token_clear() -> CmdResult<()> {
    match entry()?.delete_credential() {
        Ok(()) | Err(keyring::Error::NoEntry) => Ok(()),
        Err(e) => Err(err(e)),
    }
}

/* ---------------------------------------------------------------- GitHub */

fn agent() -> ureq::Agent {
    ureq::AgentBuilder::new().user_agent(UA).timeout(std::time::Duration::from_secs(30)).build()
}

#[derive(Serialize, Deserialize)]
pub struct DeviceCode {
    device_code: String,
    user_code: String,
    verification_uri: String,
    expires_in: u64,
    interval: u64,
}

/// Step 1 of GitHub's device flow: get a code for the user to enter on github.com/login/device.
#[tauri::command]
pub async fn gh_device_start(client_id: String) -> CmdResult<DeviceCode> {
    tauri::async_runtime::spawn_blocking(move || {
        let r = agent()
            .post("https://github.com/login/device/code")
            .set("Accept", "application/json")
            .send_form(&[("client_id", client_id.as_str()), ("scope", "repo read:user")])
            .map_err(|e| format!("GitHub didn't accept the request: {e}"))?;
        r.into_json::<DeviceCode>().map_err(|e| format!("Unexpected reply from GitHub ({e}). Check the OAuth app's Client ID and that Device Flow is enabled."))
    })
    .await
    .map_err(err)?
}

#[derive(Serialize)]
pub struct DevicePoll {
    status: String, // "ok" | "pending" | "slow_down" | "expired" | "denied" | "error"
    message: Option<String>,
}

/// Step 2: poll until the user approves. On success the token goes straight into the credential store.
#[tauri::command]
pub async fn gh_device_poll(client_id: String, device_code: String) -> CmdResult<DevicePoll> {
    tauri::async_runtime::spawn_blocking(move || {
        let r = agent()
            .post("https://github.com/login/oauth/access_token")
            .set("Accept", "application/json")
            .send_form(&[
                ("client_id", client_id.as_str()),
                ("device_code", device_code.as_str()),
                ("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
            ])
            .map_err(err)?;
        let v: serde_json::Value = r.into_json().map_err(err)?;
        if let Some(tok) = v.get("access_token").and_then(|t| t.as_str()) {
            gh_token_set(tok.to_string())?;
            return Ok(DevicePoll { status: "ok".into(), message: None });
        }
        let e = v.get("error").and_then(|e| e.as_str()).unwrap_or("error");
        let status = match e {
            "authorization_pending" => "pending",
            "slow_down" => "slow_down",
            "expired_token" => "expired",
            "access_denied" => "denied",
            _ => "error",
        };
        Ok(DevicePoll { status: status.into(), message: v.get("error_description").and_then(|d| d.as_str()).map(String::from) })
    })
    .await
    .map_err(err)?
}

/// GET/POST to api.github.com with the stored token. Returns the JSON body.
#[tauri::command]
pub async fn gh_api(method: String, path: String, body: Option<serde_json::Value>) -> CmdResult<serde_json::Value> {
    tauri::async_runtime::spawn_blocking(move || {
        let token = token_get_inner().map_err(|_| "Not signed in to GitHub".to_string())?;
        if !path.starts_with('/') {
            return Err("bad API path".into());
        }
        let url = format!("https://api.github.com{path}");
        let req = agent()
            .request(&method.to_uppercase(), &url)
            .set("Accept", "application/vnd.github+json")
            .set("X-GitHub-Api-Version", "2022-11-28")
            .set("Authorization", &format!("Bearer {token}"));
        let res = match body {
            Some(b) => req.send_json(b),
            None => req.call(),
        };
        match res {
            Ok(r) => {
                let text = r.into_string().map_err(err)?;
                Ok(if text.trim().is_empty() { serde_json::Value::Null } else { serde_json::from_str(&text).map_err(err)? })
            }
            Err(ureq::Error::Status(code, r)) => {
                let text = r.into_string().unwrap_or_default();
                let msg = serde_json::from_str::<serde_json::Value>(&text)
                    .ok()
                    .and_then(|v| {
                        let base = v.get("message")?.as_str()?.to_string();
                        let extra = v
                            .get("errors")
                            .and_then(|e| e.as_array())
                            .map(|a| a.iter().filter_map(|x| x.get("message").and_then(|m| m.as_str())).collect::<Vec<_>>().join("; "))
                            .filter(|s| !s.is_empty());
                        Some(match extra {
                            Some(x) => format!("{base}: {x}"),
                            None => base,
                        })
                    })
                    .unwrap_or(text);
                Err(format!("GitHub error {code}: {msg}"))
            }
            Err(e) => Err(format!("Couldn't reach GitHub: {e}")),
        }
    })
    .await
    .map_err(err)?
}
