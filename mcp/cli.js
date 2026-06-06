#!/usr/bin/env node

import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { execFileSync } from "node:child_process";
import * as clack from "@clack/prompts";
import boxen from "boxen";
import cfonts from "cfonts";

const SERVER_NAME = "Drishti";
const MCP_URL = "https://mcp.drishti.manasija.in";
const LOGO_COLOR = { r: 245, g: 158, b: 11 };
const LOGO_ART = [
  "⣿⣿⣿⣷⡆⢰⣾⣿⣿⣿",
  "⢿⣿⡿⢋⣤⣤⡙⢿⣿⡟",
  " ⣉ ⣿⣿⣿⣿ ⣉",
  "⣾⣿⣧⣌⠛⠛⣡⣾⣿⣷",
  "⣿⣿⣿⡿⠇⠸⢿⣿⣿⣿",
];

const HOME = os.homedir();

function isWindows() {
  return process.platform === "win32";
}

function commandExists(command) {
  try {
    const probe = isWindows() ? "where" : "which";
    const args = [command];
    execFileSync(probe, args, { stdio: "ignore" });
    return true;
  } catch {
    return false;
  }
}

function ensureDir(dir) {
  fs.mkdirSync(dir, { recursive: true });
}

function readJson(filePath) {
  try {
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
  } catch {
    return null;
  }
}

function writeJson(filePath, value) {
  ensureDir(path.dirname(filePath));
  fs.writeFileSync(filePath, `${JSON.stringify(value, null, 2)}\n`, "utf8");
}

function mergeServerConfig(containerKey, filePath, serverConfig) {
  const existing = readJson(filePath) ?? {};
  const next = {
    ...existing,
    [containerKey]: {
      ...(existing?.[containerKey] && typeof existing[containerKey] === "object"
        ? existing[containerKey]
        : {}),
      [SERVER_NAME]: serverConfig,
    },
  };
  writeJson(filePath, next);
}

function homePath(...segments) {
  return path.join(HOME, ...segments);
}

function ansi(colorCode, text) {
  return `\u001b[${colorCode}m${text}\u001b[0m`;
}

function muted(text) {
  return ansi("38;5;244", text);
}

function logoColorPrefix() {
  return `\u001b[38;2;${LOGO_COLOR.r};${LOGO_COLOR.g};${LOGO_COLOR.b}m`;
}

function renderLogoArt() {
  const prefix = logoColorPrefix();
  return LOGO_ART.map((line) => {
    return `${prefix}${line}\u001b[0m`;
  });
}

function stripAnsi(text) {
  return text.replace(/\u001b\[[0-9;]*m/g, "");
}

function visibleWidth(text) {
  return stripAnsi(text).length;
}

function padEndVisible(text, width) {
  const currentWidth = visibleWidth(text);
  return currentWidth >= width ? text : `${text}${" ".repeat(width - currentWidth)}`;
}

function joinColumns(leftLines, rightLines, gap = 2) {
  const leftWidth = Math.max(0, ...leftLines.map(visibleWidth));
  const height = Math.max(leftLines.length, rightLines.length);
  const leftOffset = Math.floor((height - leftLines.length) / 2);
  const rightOffset = Math.floor((height - rightLines.length) / 2);
  const combined = [];
  for (let row = 0; row < height; row += 1) {
    const leftIndex = row - leftOffset;
    const rightIndex = row - rightOffset;
    const left =
      leftIndex >= 0 && leftIndex < leftLines.length
        ? padEndVisible(leftLines[leftIndex], leftWidth)
        : " ".repeat(leftWidth);
    const right =
      rightIndex >= 0 && rightIndex < rightLines.length ? rightLines[rightIndex] : "";
    combined.push(`${left}${" ".repeat(gap)}${right}`);
  }
  return combined;
}

function trimEmptyLines(lines) {
  const next = [...lines];
  while (next.length > 0 && next[0].trim() === "") {
    next.shift();
  }
  while (next.length > 0 && next[next.length - 1].trim() === "") {
    next.pop();
  }
  return next;
}

function renderTitleBanner() {
  return cfonts.render(SERVER_NAME, {
    font: "chrome",
    align: "left",
    colors: ["#f59e0b"],
    background: "transparent",
    letterSpacing: 1,
    lineHeight: 0,
    space: false,
    env: "node",
  });
}

function renderHeader() {
  const logoLines = renderLogoArt();
  const bannerLines = trimEmptyLines(
    renderTitleBanner()
      .string.split("\n")
      .map((line) => line.trimEnd())
  );
  const headerLines =
    logoLines && bannerLines.length > 0
      ? joinColumns(logoLines, bannerLines)
      : logoLines ?? bannerLines;

  const content = [...headerLines, muted("MCP client setup for the Drishti server")].join("\n");

  console.log("");
  console.log(
    boxen(content, {
      padding: { top: 0, bottom: 0, left: 1, right: 1 },
      margin: { top: 0, bottom: 0, left: 0, right: 0 },
      borderStyle: "round",
      borderColor: "yellow",
    })
  );
  console.log("");
}

function renderClientDetection(detectedClients) {
  const clientList = detectedClients.length ? detectedClients.join(", ") : "none";
  clack.note(
    [
      clientList,
      "↑↓ move · Space toggle · Enter confirm",
      "◼ selected   ◻ not selected",
    ].join("\n"),
    "Detected clients"
  );
}

function vscodeConfigPath() {
  if (process.platform === "darwin") {
    return homePath("Library", "Application Support", "Code", "User", "mcp.json");
  }
  if (process.platform === "win32") {
    const appData = process.env.APPDATA ?? homePath("AppData", "Roaming");
    return path.join(appData, "Code", "User", "mcp.json");
  }
  return homePath(".config", "Code", "User", "mcp.json");
}

function zedConfigPath() {
  if (process.platform === "darwin") {
    return homePath("Library", "Application Support", "Zed", "settings.json");
  }
  if (process.platform === "win32") {
    const appData = process.env.APPDATA ?? homePath("AppData", "Roaming");
    return path.join(appData, "Zed", "settings.json");
  }
  return homePath(".config", "zed", "settings.json");
}

function codexConfigPath() {
  return homePath(".codex", "config.toml");
}

function findClaudeBinary() {
  const possiblePaths = [
    homePath(".claude", "local", "claude"),
    "/usr/local/bin/claude",
    "/opt/homebrew/bin/claude",
  ];
  if (commandExists("claude")) {
    return "claude";
  }
  for (const claudePath of possiblePaths) {
    if (fs.existsSync(claudePath)) {
      return claudePath;
    }
  }
  return null;
}

function claudeCodeConfigLabel() {
  return `claude mcp (${SERVER_NAME}, user scope)`;
}

function configureClaudeCodeMcp(apiKey) {
  const claudeBinary = findClaudeBinary();
  if (!claudeBinary) {
    throw new Error("Claude Code CLI was not found.");
  }
  try {
    execFileSync(claudeBinary, ["mcp", "remove", "--scope", "user", SERVER_NAME], {
      stdio: "ignore",
    });
  } catch {
    // Server may not exist yet.
  }
  const addArgs = [
    "mcp",
    "add",
    "--transport",
    "http",
    "--scope",
    "user",
    SERVER_NAME,
    MCP_URL,
    "--header",
    `Authorization: Bearer ${apiKey}`,
  ];
  try {
    execFileSync(claudeBinary, addArgs, { encoding: "utf8", stdio: "pipe" });
  } catch (error) {
    const stderr = typeof error?.stderr === "string" ? error.stderr.trim() : "";
    const detail = stderr || (error instanceof Error ? error.message : String(error));
    throw new Error(`Failed to configure Claude Code MCP: ${detail}`);
  }
  return claudeCodeConfigLabel();
}

function mcpAuthHeaders(apiKey) {
  return {
    Authorization: `Bearer ${apiKey}`,
  };
}

function sharedMcpServerConfig(apiKey) {
  return {
    url: MCP_URL,
    headers: mcpAuthHeaders(apiKey),
  };
}

function sharedVscodeServerConfig(apiKey) {
  return {
    type: "http",
    url: MCP_URL,
    headers: mcpAuthHeaders(apiKey),
  };
}

function sharedZedConfig(apiKey) {
  return {
    enabled: true,
    url: MCP_URL,
    headers: mcpAuthHeaders(apiKey),
  };
}

function sharedCodexConfig(apiKey) {
  const escapedApiKey = apiKey.replace(/\\/g, "\\\\").replace(/"/g, '\\"');
  return [
    `[mcp_servers.${SERVER_NAME}]`,
    `url = "${MCP_URL}"`,
    `http_headers = { Authorization = "Bearer ${escapedApiKey}" }`,
    "",
  ].join("\n");
}

function writeTomlMcpServer(filePath, blockName, blockText) {
  const blockHeader = `[mcp_servers.${blockName}]`;
  const cleanedBlock = blockText.trimEnd();
  const existing = fs.existsSync(filePath) ? fs.readFileSync(filePath, "utf8") : "";
  const lines = existing.split(/\r?\n/);
  const keptLines = [];
  let skippingTargetSection = false;
  let foundTargetSection = false;

  for (const line of lines) {
    const trimmed = line.trim();
    const isSectionHeader = /^\[[^\[\]]+\]$/.test(trimmed);
    if (isSectionHeader) {
      skippingTargetSection = trimmed === blockHeader;
      if (skippingTargetSection) {
        foundTargetSection = true;
        continue;
      }
    }
    if (!skippingTargetSection) {
      keptLines.push(line);
    }
  }

  const prefix = keptLines.join("\n").trimEnd();
  const next = prefix ? `${prefix}\n\n${cleanedBlock}\n` : `${cleanedBlock}\n`;
  ensureDir(path.dirname(filePath));
  fs.writeFileSync(filePath, next, "utf8");
}

function candidateClients() {
  return [
    {
      name: "Cursor",
      configPath: homePath(".cursor", "mcp.json"),
      containerKey: "mcpServers",
      detected: commandExists("cursor") || commandExists("cursor-agent") || fs.existsSync(homePath(".cursor", "mcp.json")),
    },
    {
      name: "VS Code",
      configPath: vscodeConfigPath(),
      containerKey: "servers",
      detected: commandExists("code") || commandExists("code-insiders") || fs.existsSync(vscodeConfigPath()),
    },
    {
      name: "Zed",
      configPath: zedConfigPath(),
      containerKey: "context_servers",
      detected: commandExists("zed") || fs.existsSync(zedConfigPath()),
    },
    {
      name: "Codex",
      configPath: codexConfigPath(),
      containerKey: "mcp_servers",
      detected: commandExists("codex") || fs.existsSync(codexConfigPath()),
    },
    {
      name: "Claude Code",
      configPath: claudeCodeConfigLabel(),
      detected: findClaudeBinary() !== null,
    },
  ];
}

function promptHidden(question) {
  if (!process.stdin.isTTY) {
    return Promise.reject(new Error("Interactive input is required for the API key."));
  }

  return new Promise((resolve, reject) => {
    const stdin = process.stdin;
    const stdout = process.stdout;
    let value = "";

    stdout.write(question);
    stdin.setRawMode(true);
    stdin.resume();
    stdin.setEncoding("utf8");

    const cleanup = () => {
      stdin.off("data", onData);
      stdin.setRawMode(false);
      stdin.pause();
    };

    const onData = (chunk) => {
      const char = String(chunk);
      if (char === "\u0003") {
        cleanup();
        reject(new Error("Cancelled"));
        return;
      }
      if (char === "\r" || char === "\n") {
        stdout.write("\n");
        cleanup();
        resolve(value);
        return;
      }
      if (char === "\u007f" || char === "\b") {
        value = value.slice(0, -1);
        return;
      }
      value += char;
    };

    stdin.on("data", onData);
  });
}

async function selectDetectedClients(clients) {
  const detected = clients.filter((client) => client.detected);
  if (detected.length === 0 || !process.stdin.isTTY || !process.stdout.isTTY) {
    return new Set(detected.map((client) => client.name));
  }
  const selection = await clack.multiselect({
    message: "Select clients to configure",
    options: detected.map((client) => ({
      value: client.name,
      label: client.name,
      hint: client.configPath,
    })),
    initialValues: detected.map((client) => client.name),
    required: false,
  });
  if (clack.isCancel(selection)) {
    throw new Error("Cancelled");
  }
  return new Set(selection);
}

function normalizeApiKey(value) {
  return value.trim();
}

async function resolveApiKey() {
  const envValue = process.env.DRISHTI_MCP_API_KEY ?? process.env.API_KEY ?? "";
  if (envValue.trim()) {
    return normalizeApiKey(envValue);
  }
  const entered = await promptHidden("Enter your Drishti API key: ");
  const apiKey = normalizeApiKey(entered);
  if (!apiKey) {
    throw new Error("The API key cannot be empty.");
  }
  return apiKey;
}

function printSummary(results) {
  console.log("");
  for (const result of results) {
    if (result.changed) {
      console.log(`Updated ${result.name}: ${result.path}`);
    } else {
      console.log(`Skipped ${result.name}: ${result.reason}`);
    }
  }
  console.log("");
  console.log(`Configured MCP server URL: ${MCP_URL}`);
  console.log("The API key was written directly into the client config header for this install.");
  console.log(`If you need to rotate it, rerun this command with a new key.`);
}

async function main() {
  renderHeader();
  const clients = candidateClients();
  const apiKey = await resolveApiKey();
  const serverConfig = sharedMcpServerConfig(apiKey);
  const vscodeConfig = sharedVscodeServerConfig(apiKey);
  const zedConfig = sharedZedConfig(apiKey);
  const codexConfig = sharedCodexConfig(apiKey);
  const results = [];
  renderClientDetection(clients.filter((client) => client.detected).map((client) => client.name));
  const selectedNames = await selectDetectedClients(clients);

  if (selectedNames.size === 0) {
    console.log("No MCP clients were selected.");
    console.log("Rerun the installer if you want to configure one later.");
    return;
  }

  for (const client of clients) {
    if (!client.detected) {
      results.push({
        name: client.name,
        changed: false,
        reason: "client not detected",
      });
      continue;
    }
    if (!selectedNames.has(client.name)) {
      results.push({
        name: client.name,
        changed: false,
        reason: "not selected",
      });
      continue;
    }

    if (client.name === "Codex") {
      writeTomlMcpServer(client.configPath, SERVER_NAME, codexConfig);
    } else if (client.name === "Claude Code") {
      configureClaudeCodeMcp(apiKey);
    } else if (client.name === "VS Code") {
      mergeServerConfig(client.containerKey, client.configPath, vscodeConfig);
    } else if (client.name === "Zed") {
      mergeServerConfig(client.containerKey, client.configPath, zedConfig);
    } else {
      mergeServerConfig(client.containerKey, client.configPath, serverConfig);
    }
    results.push({
      name: client.name,
      changed: true,
      path: client.configPath,
    });
  }

  const anyUpdated = results.some((result) => result.changed);
  if (!anyUpdated) {
    console.log("No supported MCP clients were detected.");
    console.log("Supported clients: Cursor, VS Code, Zed, Codex, Claude Code.");
    console.log(`If you want to preconfigure one manually, create the matching file and rerun this installer.`);
    return;
  }

  printSummary(results);
}

main().catch((error) => {
  const message = error instanceof Error ? error.message : String(error);
  console.error(`drishti-mcp: ${message}`);
  process.exitCode = 1;
});
