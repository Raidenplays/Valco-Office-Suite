//! W3C validation through the Nu Html Checker (validator.w3.org/nu), which checks
//! both HTML and CSS. Returns W3C's raw JSON.

type CmdResult<T> = Result<T, String>;

const UA: &str = "ValcoWeave/0.4 (+https://github.com/Raidenplays/valco-weave)";

fn agent() -> ureq::Agent {
    ureq::AgentBuilder::new().user_agent(UA).timeout(std::time::Duration::from_secs(45)).build()
}

fn friendly(e: ureq::Error) -> String {
    match e {
        ureq::Error::Status(429, _) => "The W3C validator is busy (too many requests). Wait a minute and try again.".into(),
        ureq::Error::Status(503, _) => "The W3C service is unavailable right now. Please try again later.".into(),
        ureq::Error::Status(code, _) => format!("The W3C validator answered with error {code}."),
        ureq::Error::Transport(t) => format!("Couldn't reach the W3C validator. Check your internet connection. ({t})"),
    }
}

#[tauri::command]
pub async fn w3c_validate(kind: String, text: String) -> CmdResult<String> {
    tauri::async_runtime::spawn_blocking(move || {
        let content_type = if kind == "css" { "text/css; charset=utf-8" } else { "text/html; charset=utf-8" };
        agent()
            .post("https://validator.w3.org/nu/?out=json")
            .set("Content-Type", content_type)
            .send_string(&text)
            .map_err(friendly)?
            .into_string()
            .map_err(|e| e.to_string())
    })
    .await
    .map_err(|e| e.to_string())?
}
