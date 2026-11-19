//! Valco Weave desktop backend: file system access for the open site, opening files
//! in the default browser, and FTP / FTPS publishing.

mod git;
mod w3c;

use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::UNIX_EPOCH;
use tauri::ipc::{InvokeBody, Request, Response};
use tauri::{AppHandle, Emitter};
use tauri_plugin_opener::OpenerExt;

type CmdResult<T> = Result<T, String>;

fn err<E: std::fmt::Display>(e: E) -> String {
    e.to_string()
}

/* ------------------------------------------------------------------ files */

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Entry {
    name: String,
    path: String,
    is_dir: bool,
    size: Option<u64>,
    mtime: Option<u64>,
}

fn to_slash(p: &Path) -> String {
    p.to_string_lossy().replace('\\', "/")
}

fn entry_for(path: &Path) -> Option<Entry> {
    let meta = fs::metadata(path).ok()?;
    let mtime = meta
        .modified()
        .ok()
        .and_then(|t| t.duration_since(UNIX_EPOCH).ok())
        .map(|d| d.as_millis() as u64);
    Some(Entry {
        name: path.file_name()?.to_string_lossy().into_owned(),
        path: to_slash(path),
        is_dir: meta.is_dir(),
        size: if meta.is_file() { Some(meta.len()) } else { None },
        mtime,
    })
}

#[tauri::command]
fn fs_read_dir(path: String) -> CmdResult<Vec<Entry>> {
    let mut out: Vec<Entry> = fs::read_dir(&path)
        .map_err(err)?
        .filter_map(|e| e.ok())
        .filter_map(|e| entry_for(&e.path()))
        .collect();
    out.sort_by(|a, b| b.is_dir.cmp(&a.is_dir).then(a.name.to_lowercase().cmp(&b.name.to_lowercase())));
    Ok(out)
}

const SKIP_DIRS: &[&str] = &[".git", "node_modules", ".weavecraft", ".svn", ".hg"];

#[tauri::command]
fn fs_list_all(root: String) -> CmdResult<Vec<Entry>> {
    let mut out = Vec::new();
    let mut stack = vec![(PathBuf::from(&root), 0usize)];
    while let Some((dir, depth)) = stack.pop() {
        if depth > 16 || out.len() > 50_000 {
            break;
        }
        let rd = match fs::read_dir(&dir) {
            Ok(r) => r,
            Err(_) => continue,
        };
        for e in rd.filter_map(|e| e.ok()) {
            let p = e.path();
            let name = e.file_name().to_string_lossy().into_owned();
            let ft = match e.file_type() {
                Ok(t) => t,
                Err(_) => continue,
            };
            if ft.is_dir() {
                if !SKIP_DIRS.contains(&name.as_str()) {
                    stack.push((p, depth + 1));
                }
            } else if ft.is_file() && name != ".DS_Store" && name != "Thumbs.db" {
                if let Some(en) = entry_for(&p) {
                    out.push(en);
                }
            }
        }
    }
    Ok(out)
}

#[tauri::command]
fn fs_read_text(path: String) -> CmdResult<String> {
    let bytes = fs::read(&path).map_err(err)?;
    Ok(String::from_utf8_lossy(&bytes).into_owned())
}

#[tauri::command]
fn fs_read_bytes(path: String) -> CmdResult<Response> {
    Ok(Response::new(fs::read(&path).map_err(err)?))
}

fn ensure_parent(path: &Path) -> CmdResult<()> {
    if let Some(parent) = path.parent() {
        if !parent.as_os_str().is_empty() {
            fs::create_dir_all(parent).map_err(err)?;
        }
    }
    Ok(())
}

/// Write via a temp file + rename so a crash never leaves a half-written page.
fn write_atomic(path: &Path, data: &[u8]) -> CmdResult<()> {
    ensure_parent(path)?;
    let tmp = path.with_file_name(format!(
        ".{}.weavecraft-tmp",
        path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default()
    ));
    fs::write(&tmp, data).map_err(err)?;
    if let Err(e) = fs::rename(&tmp, path) {
        // rename can fail across some network shares: fall back to a direct write
        let _ = fs::remove_file(&tmp);
        fs::write(path, data).map_err(|e2| format!("{e}; {e2}"))?;
    }
    Ok(())
}

#[tauri::command]
fn fs_write_text(path: String, text: String) -> CmdResult<()> {
    write_atomic(Path::new(&path), text.as_bytes())
}

#[tauri::command]
fn fs_write_bytes(request: Request<'_>) -> CmdResult<()> {
    let raw_path = request
        .headers()
        .get("x-path")
        .ok_or("missing x-path header")?
        .to_str()
        .map_err(err)?;
    let path = percent_encoding::percent_decode_str(raw_path).decode_utf8().map_err(err)?.into_owned();
    match request.body() {
        InvokeBody::Raw(bytes) => write_atomic(Path::new(&path), bytes),
        InvokeBody::Json(v) => {
            let bytes: Vec<u8> = serde_json::from_value(v.clone()).map_err(err)?;
            write_atomic(Path::new(&path), &bytes)
        }
    }
}

#[tauri::command]
fn fs_mkdir(path: String) -> CmdResult<()> {
    fs::create_dir_all(&path).map_err(err)
}

#[tauri::command]
fn fs_rename(from: String, to: String) -> CmdResult<()> {
    let to = Path::new(&to);
    ensure_parent(to)?;
    fs::rename(&from, to).map_err(err)
}

#[tauri::command]
fn fs_remove(path: String) -> CmdResult<()> {
    let p = Path::new(&path);
    if p.is_dir() {
        fs::remove_dir_all(p).map_err(err)
    } else {
        fs::remove_file(p).map_err(err)
    }
}

#[tauri::command]
fn fs_exists(path: String) -> bool {
    Path::new(&path).exists()
}

#[tauri::command]
fn open_external(app: AppHandle, path: String) -> CmdResult<()> {
    let lower = path.to_ascii_lowercase();
    if lower.starts_with("http://") || lower.starts_with("https://") || lower.starts_with("mailto:") {
        app.opener().open_url(path, None::<&str>).map_err(err)
    } else {
        app.opener().open_path(path, None::<&str>).map_err(err)
    }
}

/* -------------------------------------------------------------------- FTP */

#[derive(Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
struct FtpConfig {
    host: String,
    port: u16,
    user: String,
    remote_dir: String,
    secure: bool,
    passive: bool,
}

#[derive(Serialize, Clone)]
struct Progress {
    done: usize,
    total: usize,
    file: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    error: Option<String>,
}

use suppaftp::types::FileType;
use suppaftp::{Mode, RustlsConnector, RustlsFtpStream};

fn connect(cfg: &FtpConfig, password: &str) -> CmdResult<RustlsFtpStream> {
    let host = cfg.host.trim();
    if host.is_empty() {
        return Err("No FTP host set".into());
    }
    let mut ftp = RustlsFtpStream::connect((host, cfg.port)).map_err(|e| format!("Could not connect to {host}:{}: {e}", cfg.port))?;
    if cfg.secure {
        let roots = rustls::RootCertStore::from_iter(webpki_roots::TLS_SERVER_ROOTS.iter().cloned());
        let provider = Arc::new(rustls::crypto::ring::default_provider());
        let config = rustls::ClientConfig::builder_with_provider(provider)
            .with_safe_default_protocol_versions()
            .map_err(err)?
            .with_root_certificates(roots)
            .with_no_client_auth();
        ftp = ftp
            .into_secure(RustlsConnector::from(Arc::new(config)), host)
            .map_err(|e| format!("TLS failed (try turning off FTPS if your host doesn't support it): {e}"))?;
    }
    let user = if cfg.user.is_empty() { "anonymous" } else { cfg.user.as_str() };
    ftp.login(user, password).map_err(|e| format!("Login failed: {e}"))?;
    ftp.set_mode(if cfg.passive { Mode::Passive } else { Mode::Active });
    ftp.set_passive_nat_workaround(true);
    ftp.transfer_type(FileType::Binary).map_err(err)?;
    Ok(ftp)
}

/// cd into an absolute remote dir, creating missing folders on the way.
fn ensure_remote_dir(ftp: &mut RustlsFtpStream, dir: &str) -> CmdResult<()> {
    let absolute = dir.starts_with('/');
    if absolute {
        ftp.cwd("/").map_err(err)?;
    }
    for seg in dir.split('/').filter(|s| !s.is_empty() && *s != ".") {
        if ftp.cwd(seg).is_err() {
            ftp.mkdir(seg).map_err(|e| format!("Could not create folder {seg}: {e}"))?;
            ftp.cwd(seg).map_err(err)?;
        }
    }
    Ok(())
}

#[tauri::command]
async fn ftp_test(cfg: FtpConfig, password: String) -> CmdResult<String> {
    tauri::async_runtime::spawn_blocking(move || {
        let mut ftp = connect(&cfg, &password)?;
        let start = ftp.pwd().unwrap_or_else(|_| "/".into());
        ensure_remote_dir(&mut ftp, &cfg.remote_dir).map_err(|e| format!("Connected, but the remote folder failed: {e}"))?;
        let here = ftp.pwd().unwrap_or_default();
        let _ = ftp.quit();
        Ok(format!("Connected. Home folder {start}, publishing to {here}"))
    })
    .await
    .map_err(err)?
}

#[tauri::command]
async fn ftp_publish(app: AppHandle, cfg: FtpConfig, password: String, root: String, files: Vec<String>) -> CmdResult<()> {
    tauri::async_runtime::spawn_blocking(move || {
        let total = files.len();
        let emit = |done: usize, file: &str, error: Option<String>| {
            let _ = app.emit("publish-progress", Progress { done, total, file: file.to_string(), error });
        };
        emit(0, "", None);
        let mut ftp = connect(&cfg, &password)?;
        let base = cfg.remote_dir.trim_end_matches('/').to_string();
        let base = if base.is_empty() { "/".to_string() } else { base };
        let mut current_dir: Option<String> = None;
        let mut sorted = files.clone();
        sorted.sort();
        for (i, rel) in sorted.iter().enumerate() {
            let rel = rel.replace('\\', "/");
            if rel.starts_with("..") || rel.contains("/../") {
                emit(i + 1, &rel, Some("skipped: outside the site folder".into()));
                continue;
            }
            let (sub, name) = match rel.rfind('/') {
                Some(ix) => (rel[..ix].to_string(), rel[ix + 1..].to_string()),
                None => (String::new(), rel.clone()),
            };
            let target_dir = if sub.is_empty() { base.clone() } else { format!("{}/{}", base.trim_end_matches('/'), sub) };
            let result = (|| -> CmdResult<()> {
                if current_dir.as_deref() != Some(target_dir.as_str()) {
                    ensure_remote_dir(&mut ftp, &target_dir)?;
                    current_dir = Some(target_dir.clone());
                }
                let local = Path::new(&root).join(&rel);
                let mut f = fs::File::open(&local).map_err(err)?;
                ftp.put_file(&name, &mut f).map_err(err)?;
                Ok(())
            })();
            match result {
                Ok(()) => emit(i + 1, &rel, None),
                Err(e) => {
                    current_dir = None;
                    emit(i + 1, &rel, Some(e));
                }
            }
        }
        let _ = ftp.quit();
        Ok(())
    })
    .await
    .map_err(err)?
}

/* ------------------------------------------------------------------- app */

#[cfg_attr(mobile, tauri::mobile_entry_point)]
/// The folder or file Valco Weave was started with (Explorer's "Open as site in
/// Valco Weave", "Open with", or a drag onto the exe), if any.
#[tauri::command]
fn launch_arg() -> Option<String> {
    std::env::args_os()
        .skip(1)
        .map(|a| a.to_string_lossy().trim_matches('"').to_string())
        .find(|a| !a.is_empty() && !a.starts_with('-'))
        .map(|a| {
            let p = std::path::Path::new(&a);
            std::fs::canonicalize(p)
                .map(|c| c.to_string_lossy().trim_start_matches(r"\\?\").to_string())
                .unwrap_or(a)
        })
}


/// Checks the Valco Office GitHub releases for a newer suite version. Each release is one ValcoOffice-Setup.exe
/// tagged v<suite version>; the tag is read from GitHub's /releases/latest redirect.
/// Returns { ok, available, latest, installed, error }.
#[tauri::command]
fn valco_update_check() -> serde_json::Value {
    const LATEST: &str = "https://github.com/Raidenplays/Valco-Office-Suite/releases/latest";
    let installed = installed_version();
    let agent = ureq::AgentBuilder::new().redirects(0).timeout(std::time::Duration::from_secs(20)).build();
    let loc = match agent.get(LATEST).set("User-Agent", "ValcoOffice/1.0").call() {
        Ok(r) => r.header("location").map(|s| s.to_string()),
        Err(ureq::Error::Status(_, r)) => r.header("location").map(|s| s.to_string()),
        Err(e) => return serde_json::json!({ "ok": false, "available": false, "installed": installed, "error": e.to_string() }),
    };
    match loc.filter(|l| l.contains("/releases/tag/")) {
        None => serde_json::json!({ "ok": false, "available": false, "installed": installed, "error": "No Valco Office release has been published yet." }),
        Some(l) => {
            let latest = l.rsplit('/').next().unwrap_or("").trim_start_matches(|c| c == 'v' || c == 'V').to_string();
            let available = match &installed { Some(i) => parse_ver(&latest) > parse_ver(i), None => true };
            serde_json::json!({ "ok": true, "available": available, "latest": latest, "installed": installed.unwrap_or_else(|| "not installed with Valco Office Setup".into()) })
        }
    }
}

fn parse_ver(v: &str) -> Vec<u64> {
    let mut p: Vec<u64> = v.split(|c| c == '.' || c == '-' || c == '+').take(4).map(|x| x.parse().unwrap_or(0)).collect();
    while p.len() < 4 { p.push(0); }
    p
}

/// Version recorded by Valco Office Setup (HKLM\SOFTWARE\Valco\Office\Apps\weave).
fn installed_version() -> Option<String> {
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        let o = std::process::Command::new("reg")
            .args(["query", r"HKLM\SOFTWARE\Valco\Office", "/v", "Version", "/reg:64"])
            .creation_flags(0x0800_0000) // CREATE_NO_WINDOW
            .output()
            .ok()?;
        let text = String::from_utf8_lossy(&o.stdout).to_string();
        let line = text.lines().find(|l| l.contains("REG_SZ"))?;
        let idx = line.find("REG_SZ")?;
        Some(line[idx + 6..].trim().to_string()).filter(|s| !s.is_empty())
    }
    #[cfg(not(windows))]
    {
        None
    }
}

/// Opens Valco Office Update (`<InstallRoot>\Setup\setup.exe /update`). Returns false when Valco Office Setup isn't installed.
#[tauri::command]
fn valco_check_updates() -> bool {
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        let out = std::process::Command::new("reg")
            .args(["query", r"HKLM\SOFTWARE\Valco\Office", "/v", "InstallRoot", "/reg:64"])
            .creation_flags(0x0800_0000)
            .output();
        if let Ok(o) = out {
            let text = String::from_utf8_lossy(&o.stdout);
            if let Some(line) = text.lines().find(|l| l.contains("InstallRoot")) {
                if let Some(idx) = line.find("REG_SZ") {
                    let root = line[idx + 6..].trim();
                    let setup = std::path::Path::new(root).join("Setup").join("setup.exe");
                    if setup.exists() {
                        return std::process::Command::new(setup).arg("/update").spawn().is_ok();
                    }
                }
            }
        }
        false
    }
    #[cfg(not(windows))]
    {
        false
    }
}

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            fs_read_dir,
            fs_list_all,
            fs_read_text,
            fs_read_bytes,
            fs_write_text,
            fs_write_bytes,
            fs_mkdir,
            fs_rename,
            fs_remove,
            fs_exists,
            open_external,
            ftp_test,
            ftp_publish,
            git::git_exec,
            git::gh_token_get,
            git::gh_token_set,
            git::gh_token_clear,
            git::gh_device_start,
            git::gh_device_poll,
            git::gh_api,
            w3c::w3c_validate,
            launch_arg,
            valco_check_updates,
            valco_update_check
        ])
        .run(tauri::generate_context!())
        .expect("error while running Valco Weave");
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Runs against a local FTP server when WC_FTP_TEST_PORT is set
    /// (e.g. `python3 -m pyftpdlib -p 2121 -w -d /tmp/ftp-root`).
    #[test]
    fn publish_to_local_ftp() {
        let Ok(port) = std::env::var("WC_FTP_TEST_PORT") else { return };
        let cfg = FtpConfig {
            host: "127.0.0.1".into(),
            port: port.parse().unwrap(),
            user: "anonymous".into(),
            remote_dir: "/site/www".into(),
            secure: false,
            passive: true,
        };
        let root = std::env::temp_dir().join("wc-ftp-src");
        fs::create_dir_all(root.join("css")).unwrap();
        fs::write(root.join("index.html"), "<h1>hi</h1>").unwrap();
        fs::write(root.join("css/style.css"), "h1{color:red}").unwrap();
        let mut ftp = connect(&cfg, "guest").unwrap();
        for rel in ["css/style.css", "index.html"] {
            let (sub, name) = rel.rsplit_once('/').map(|(a, b)| (a.to_string(), b.to_string())).unwrap_or((String::new(), rel.to_string()));
            let dir = if sub.is_empty() { cfg.remote_dir.clone() } else { format!("{}/{}", cfg.remote_dir, sub) };
            ensure_remote_dir(&mut ftp, &dir).unwrap();
            let mut f = fs::File::open(root.join(rel)).unwrap();
            ftp.put_file(&name, &mut f).unwrap();
        }
        ftp.quit().unwrap();
    }
}
