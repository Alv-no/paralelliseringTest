// PreToolUse-hook: spør brukeren før et verktøykall leser eller skriver utenfor tillatte mapper.
//
// Mappelisten står i path-guard.json ved siden av denne fila.
//
// Filverktøyene (Read, Write, Edit, Glob, Grep, NotebookEdit) har stien i et eget felt, så der
// er sjekken presis. Bash og PowerShell er fritekst: stier letes fram med mønstre, og en sti
// som bygges i en variabel eller kommer fra $(...) slipper gjennom. Det er et sikkerhetsnett,
// ikke en vegg.
//
// Ved tvil, eller hvis skriptet selv feiler, svarer det «ask» — aldri stille «ok».

import { readFileSync } from "node:fs";
import { dirname, join, win32 } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));

function expandEnv(s) {
  return s
    .replace(/%([A-Za-z_][A-Za-z0-9_]*)%/g, (m, n) => process.env[n] ?? m)
    .replace(/\$env:([A-Za-z_][A-Za-z0-9_]*)/gi, (m, n) => process.env[n] ?? process.env[n.toUpperCase()] ?? m)
    .replace(/\$\{([A-Za-z_][A-Za-z0-9_]*)\}/g, (m, n) => process.env[n] ?? m)
    .replace(/\$(HOME|USERPROFILE|TEMP|TMP|APPDATA|LOCALAPPDATA|PROGRAMDATA)\b/g, (m, n) =>
      n === "HOME" ? (process.env.USERPROFILE ?? m) : (process.env[n] ?? m),
    );
}

// Gjør en sti om til absolutt Windows-form: /c/x -> C:\x, ~ -> hjemmemappa, relativ -> mot cwd.
function normalize(p, cwd) {
  let s = expandEnv(p.trim().replace(/^["']|["']$/g, ""));
  if (/^~(?=[\\/]|$)/.test(s)) s = (process.env.USERPROFILE ?? "") + s.slice(1);
  const msys = s.match(/^\/([a-zA-Z])(?=\/|$)(.*)$/);
  if (msys) s = `${msys[1].toUpperCase()}:${msys[2] || "\\"}`;
  else if (/^\/(tmp|usr|etc|bin|mingw64|ucrt64|opt|var|home)(\/|$)/.test(s)) {
    // Git Bash sin egen rot — ligger utenfor alle prosjektmapper.
    return `[Git Bash]${s}`;
  }
  return win32.resolve(cwd || process.cwd(), s);
}

// Fjerner et tillatt skript der det står som kommandoen som kjøres — først i et kommandoledd,
// eventuelt etter en tolk. Står samme sti andre steder (`> skript.sh`, `cp x skript.sh`), blir
// den stående og sjekkes som vanlig. Det er derfor kjøring kan være fri mens redigering spør.
function stripRunnable(cmd, runnable, cwd) {
  if (runnable.length === 0) return cmd;
  // `&&`/`||` først, ellers leses andre `&` i `&&` som PowerShells kall-operator.
  const start = String.raw`(^|(?:&&|\|\||[;&|\n(])\s*)`;
  const interp = String.raw`((?:(?:bash|sh|node|pwsh|powershell|python3?|&)(?:\.exe)?\s+(?:-(?:File|NoProfile|ExecutionPolicy\s+\S+)\s+)*)?)`;
  const token = String.raw`("[^"]+"|'[^']+'|[^\s;&|]+)`;
  return cmd.replace(new RegExp(start + interp + token, "gi"), (all, pre, int, tok) => {
    const abs = normalize(tok, cwd).toLowerCase();
    return runnable.includes(abs) ? `${pre}${int}` : all;
  });
}

function isInside(abs, roots) {
  const a = abs.toLowerCase().replace(/[\\/]+$/, "");
  return roots.some((r) => a === r || a.startsWith(r + "\\"));
}

// Stikandidater i en shell-kommando. Bevisst grovt: heller et spørsmål for mye.
function pathsInCommand(cmd) {
  const found = new Set();
  // Venstresiden i en tilordning er et navn, ikke en sti: `$env:TEMP='C:\x'` skal sjekke C:\x,
  // ikke verdien TEMP har fra før. Uten dette ble `$env:TEMP=` utvidet til `...\Temp=`.
  const unassigned = cmd
    .replace(/\$env:[A-Za-z_][A-Za-z0-9_]*\s*=/gi, " ")
    .replace(/\b(?:export|set|setx)\s+[A-Za-z_][A-Za-z0-9_]*\s*=?/gi, " ");
  const text = expandEnv(unassigned);
  const boundary = String.raw`(?:^|[\s"'=(<>|;&,])`;
  const patterns = [
    String.raw`[A-Za-z]:[\\/][^\s"'|;&<>()]*`, // C:\x eller C:/x
    String.raw`\\\\[^\s"'|;&<>()]+`, // UNC \\server\share
    String.raw`/[a-zA-Z](?=/|[\s"']|$)[^\s"'|;&<>()]*`, // /c/x (Git Bash)
    String.raw`/(?:tmp|usr|etc|bin|mingw64|ucrt64|opt|var|home)(?=/|[\s"']|$)[^\s"'|;&<>()]*`,
    String.raw`~(?=[\\/]|[\s"']|$)[^\s"'|;&<>()]*`, // ~ og ~/x
    String.raw`[^\s"'|;&<>()]*\.\.(?:[\\/][^\s"'|;&<>()]*)?`, // alt med .. i
  ];
  for (const p of patterns) {
    const re = new RegExp(boundary + "(" + p + ")", "g");
    for (const m of text.matchAll(re)) {
      // Tegnsetting som avslutter en sti i en liste eller tilordning (`a.cs,` `x=`) er ikke en del av den.
      const p = m[1].replace(/[=,;:]+$/, "");
      if (p) found.add(p);
    }
  }
  return [...found];
}

function pathsInFileTool(name, input) {
  const out = [];
  for (const key of ["file_path", "notebook_path", "path"]) {
    if (typeof input[key] === "string" && input[key]) out.push(input[key]);
  }
  // Glob/Grep uten path søker i cwd; et absolutt mønster peker selv ut hvor.
  if ((name === "Glob" || name === "Grep") && !input.path) {
    const pat = typeof input.pattern === "string" ? input.pattern : "";
    out.push(/^([A-Za-z]:[\\/]|\/[a-zA-Z]\/|~)/.test(pat) ? pat.replace(/[*?[{].*$/, "") || "." : ".");
  }
  return out;
}

function respond(reason) {
  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: {
        hookEventName: "PreToolUse",
        permissionDecision: "ask",
        permissionDecisionReason: reason,
      },
    }),
  );
  process.exit(0);
}

let raw = "";
try {
  raw = readFileSync(0, "utf8");
  const event = JSON.parse(raw);
  const config = JSON.parse(readFileSync(join(here, "path-guard.json"), "utf8"));
  const toRoot = (r) => win32.resolve(expandEnv(r)).toLowerCase().replace(/[\\/]+$/, "");
  const roots = config.allowed.map(toRoot);
  // Kun lesing: Read/Glob/Grep går gjennom, alt som kan skrive spør. Shell regnes alltid som
  // skriving, fordi teksten ikke sier om `cat` leser eller `>` skriver.
  const readRoots = (config.readOnly ?? []).map(toRoot);
  // Prosjektet økta jobber i er alltid tillatt — men aldri en hel disk eller hele hjemmemappa,
  // som ellers ville åpnet alt for en økt som tilfeldigvis ble startet der.
  const projectDir = config.allowProjectDir !== false && (process.env.CLAUDE_PROJECT_DIR || event.cwd);
  if (projectDir) {
    const p = win32.resolve(projectDir).toLowerCase().replace(/[\\/]+$/, "");
    const home = win32.resolve(process.env.USERPROFILE ?? "").toLowerCase();
    if (!/^[a-z]:$/.test(p) && p !== home) roots.push(p);
  }
  const alwaysOk = new Set((config.alwaysOk ?? []).map((s) => s.toLowerCase()));
  const cwd = event.cwd;
  const name = event.tool_name;
  const input = event.tool_input ?? {};

  const isShell = name === "Bash" || name === "PowerShell";
  const readsOnly = name === "Read" || name === "Glob" || name === "Grep";
  const runnable = (config.runnable ?? []).map((r) => normalize(r, cwd).toLowerCase());
  const candidates = isShell
    ? pathsInCommand(stripRunnable(String(input.command ?? ""), runnable, cwd))
    : pathsInFileTool(name, input);

  const outside = [];
  for (const c of candidates) {
    if (alwaysOk.has(c.toLowerCase())) continue;
    const abs = normalize(c, cwd);
    if (abs.startsWith("[Git Bash]")) outside.push(abs);
    else if (isInside(abs, roots)) continue;
    else if (isInside(abs, readRoots)) {
      if (!readsOnly) outside.push(`${abs} (kun lesetilgang)`);
    } else outside.push(abs);
  }
  // En shell-kommando kjører i cwd; ligger den selv utenfor, gjelder det alle relative stier.
  if (isShell && cwd && !isInside(win32.resolve(cwd), roots)) outside.push(`${win32.resolve(cwd)} (arbeidsmappe)`);

  if (outside.length === 0) process.exit(0);
  // Dialogen viser ren tekst — ingen farger eller formatering. Emoji og versaler først er det
  // som faktisk synes. Shell kan ikke klassifiseres: teksten sier ikke om `cat` leser eller `>` skriver.
  const headline = isShell
    ? "🔴 SKUMMEL SHELL-KOMMANDO (kan lese eller skrive)"
    : readsOnly
      ? "🔴 SKUMMEL LESING"
      : "🔴 SKUMMEL SKRIVING";
  respond(
    `${headline}\n\n` +
      `path-guard: ${name} rører ${outside.length === 1 ? "en sti" : "stier"} utenfor tillatte mapper:\n` +
      [...new Set(outside)].map((p) => `  - ${p}`).join("\n") +
      `\nTillatt: prosjektmappa, ${config.allowed.join(", ")}` +
      (readRoots.length ? `; kun lesing: ${config.readOnly.join(", ")}` : "") +
      ` (rediger ${join(here, "path-guard.json")})`,
  );
} catch (err) {
  respond(`path-guard kunne ikke vurdere kallet (${err.message}) — spør for sikkerhets skyld.`);
}
