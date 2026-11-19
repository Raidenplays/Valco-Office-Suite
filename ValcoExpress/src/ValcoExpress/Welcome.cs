using System;
using System.IO;
using MimeKit;

namespace ValcoExpress
{
    static class Welcome
    {
        public static void Seed(string inboxDir)
        {
            var m = new MimeMessage();
            m.From.Add(new MailboxAddress("The Valco Express Team", "welcome@valco.invalid"));
            m.To.Add(new MailboxAddress("New Valco Express User", "you@valco.invalid"));
            m.Subject = "Welcome to Valco Express";
            m.Date = DateTimeOffset.Now;
            var b = new BodyBuilder
            {
                HtmlBody = @"<html><body style=""font-family:Calibri,'Segoe UI',sans-serif;font-size:11pt;margin:0"">
<div style=""background:linear-gradient(#5fa8e6,#1d5fae);color:#fff;padding:22px 26px;font-family:'Segoe UI';"">
 <div style=""font-size:24pt;font-weight:300"">Welcome to Valco Express</div>
 <div style=""font-size:11pt;opacity:.9"">Your e-mail, the way it looked in 2007.</div>
</div>
<div style=""padding:16px 26px;line-height:1.45"">
<p>Thanks for installing Valco Express. Here are a few things to get you started:</p>
<h3 style=""color:#1d4f91;font-family:'Segoe UI';font-weight:normal;font-size:13pt;margin-bottom:4px"">Set up your e-mail account</h3>
<p style=""margin-top:0"">Open the <b>Tools</b> menu and choose <b>Accounts...</b>, then click <b>Add</b>. Valco Express talks to any IMAP and SMTP server.
For Gmail, Yahoo and iCloud, turn on two-step verification and create an <b>app password</b> to use here instead of your normal password.</p>
<h3 style=""color:#1d4f91;font-family:'Segoe UI';font-weight:normal;font-size:13pt;margin-bottom:4px"">Find your way around</h3>
<ul style=""margin-top:0"">
<li><b>Create Mail</b> (Ctrl+N) writes a new message. <b>Reply</b> (Ctrl+R), <b>Reply All</b> (Ctrl+Shift+R) and <b>Forward</b> (Ctrl+F) work on the selected message.</li>
<li><b>Send/Receive</b> (Ctrl+M) sends everything in your Outbox and checks every account for new mail.</li>
<li>Drag messages onto a folder to move them. Right-click a folder to create sub-folders.</li>
<li>Click the flag column to flag a message for follow-up.</li>
<li>Pictures in HTML messages are blocked until you click the yellow bar, so senders can't tell when you've read their mail.</li>
</ul>
<h3 style=""color:#1d4f91;font-family:'Segoe UI';font-weight:normal;font-size:13pt;margin-bottom:4px"">Local Folders</h3>
<p style=""margin-top:0"">Messages in Local Folders live on this computer only. Drag a message here from an account to keep an offline copy.</p>
<p style=""color:#666;font-size:9pt;margin-top:24px"">Valco Express is an independent project and is not affiliated with Microsoft.</p>
</div></body></html>",
                TextBody = "Welcome to Valco Express!\r\n\r\nOpen Tools > Accounts... and click Add to set up your e-mail account."
            };
            m.Body = b.ToMessageBody();
            var name = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-welcome.eml";
            using (var fs = File.Create(Path.Combine(inboxDir, name))) m.WriteTo(fs);
        }
    }
}
