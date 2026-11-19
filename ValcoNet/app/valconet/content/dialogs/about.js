"use strict";
const { VALCONET_VERSION } = ChromeUtils.importESModule("resource://valconet/ValcoNet.sys.mjs");
$("version").textContent = VALCONET_VERSION;
$("engine").textContent = `Gecko ${Services.appinfo.platformVersion} (${Services.appinfo.platformBuildID}), ${Services.appinfo.XPCOMABI}`;
$("ok").onclick = () => window.close();
const opener = DialogArgs.opener;
$("licenses").onclick = e => { e.preventDefault(); opener?.ValcoNetWindow.openURL("about:license", "tab"); window.close(); };
$("site").onclick = e => { e.preventDefault(); opener?.ValcoNetWindow.openURL("https://github.com/Raidenplays/valconet", "tab"); window.close(); };
