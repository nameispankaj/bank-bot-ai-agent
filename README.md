# Bank Bot – Mini Banking Console App (C#)

A small C# console “banking assistant” that supports basic banking commands (login, balance, deposit, withdraw, logout) and routes non-banking prompts to an Azure AI Agent.

This mini project demonstrates:
- Stateful **banking logic** (balance, login state, UPI PIN tries, transaction blocking)
- **Command parsing** using regular expressions (Regex)
- Integrating **Azure AI Projects** using **AzureCliCredential** (no API keys stored in code)

---

## Features

### Banking
- Login (fixed demo credentials)
- Check balance
- Deposit/Add money (requires UPI PIN)
- Withdraw money (requires UPI PIN and sufficient balance)
- Logout
- UPI PIN security rules:
  - PIN must be exactly **4 digits**
  - **3 tries** allowed
  - After 3 failed PIN attempts, **transactions are blocked**

### AI fallback (Azure AI Agent)
If the input is not recognized as a banking command, the app forwards the message to the configured Azure AI agent and prints the response.

---

## Command Reference

Supported commands:

- `login <username> <password>`
- `balance`
- `deposit <amount> [pin <1234>]`
- `add <amount> [pin <1234>]` (alias for deposit)
- `withdraw <amount> [pin <1234>]`
- `logout`
- `exit`

### Examples

```text
You: login admin Admin@123
Bank Bot: Login successful.

You: balance
Bank Bot: Current balance: 1000.

You: deposit 500 pin 1234
Bank Bot: Amount added successfully. New balance: 1500.

You: withdraw 200
UPI PIN (4 digits): 1234
Bank Bot: Withdrawal successful. New balance: 1300.

You: logout
Bank Bot: Logged out.

You: exit
```

---

## Configuration (No secrets in code)

This project **does not hardcode API keys**. It uses `AzureCliCredential`, which authenticates via your local Azure CLI login.

You must configure the Azure AI Project endpoint (and optionally the model) using **environment variables**.

### Required environment variable

- `AZURE_AI_PROJECT_ENDPOINT`  
  Example format:
  `https://<your-resource>.services.ai.azure.com/api/projects/<your-project>`

### Optional environment variable

- `AZURE_AI_MODEL`  
  Default: `gpt-4.1-nano`

---

## Setup

### 1) Prerequisites
- .NET SDK (recommended: .NET 8 or later)
- Azure CLI installed
- Access to an Azure AI Project

### 2) Azure login (used by AzureCliCredential)
```bash
az login
```

### 3) Set environment variables

#### Windows PowerShell
```powershell
$env:AZURE_AI_PROJECT_ENDPOINT="https://<your-resource>.services.ai.azure.com/api/projects/<your-project>"
$env:AZURE_AI_MODEL="gpt-4.1-nano"
```

#### macOS / Linux (bash/zsh)
```bash
export AZURE_AI_PROJECT_ENDPOINT="https://<your-resource>.services.ai.azure.com/api/projects/<your-project>"
export AZURE_AI_MODEL="gpt-4.1-nano"
```

### 4) Restore and run
```bash
dotnet restore
dotnet run
```

---

## How It Works (High-Level Flow)

1. The app creates:
   - A `BankTool_v2` instance (holds balance + login + UPI security state)
   - An Azure AI agent session (`AgentSession`) for non-banking prompts

2. The main loop:
   - Reads user input
   - If the input is a banking command, it executes locally
   - Otherwise, it sends the message to the Azure AI agent

---

## Code Overview

### `BankTool_v2` (Banking logic)

**State**
- `_isLoggedIn` — whether the user is logged in
- `_loginAttempts` — failed login attempts counter (max 3)
- `_balance` — starts at `1000` (in-memory)
- `_upiTriesLeft` — starts at `3`
- `_transactionsBlocked` — becomes `true` after 3 wrong PIN attempts

**Demo constants (for mini-project use only)**
- Username: `admin`
- Password: `Admin@123`
- UPI PIN: `1234`

**Key behaviors**
- Balance check requires login
- Deposit/withdraw require login + correct PIN + unblocked transactions
- 3 wrong PIN attempts blocks transactions until app restart (current design)

### `Program` (Console + parsing + AI)
- Uses Regex to parse commands:
  - `login <u> <p>`
  - `logout`
  - `balance`
  - `deposit/add <amount> ...`
  - `withdraw <amount> ...`
- If no command matches, calls the Azure AI agent via `RunAsync`

---

## Security Notes (Important)

This is a **learning / demo** project.

- The banking credentials and UPI PIN are intentionally **hardcoded demo values**.
- Account data is stored **in memory** only (resets when the app restarts).
- No encryption, no database, no real banking integration.

Do **not** use this code as-is for real financial or production systems.

---

## Troubleshooting

### `Missing environment variable: AZURE_AI_PROJECT_ENDPOINT`
Set the required environment variable before running the app (see Configuration section).

### Azure CLI authentication issues
- Ensure you ran `az login`
- Ensure your Azure account has access to the target Azure AI Project
- Try:
  ```bash
  az account show
  ```

---

## Possible Improvements
- Move demo banking credentials/UPI PIN to user registration + secure storage
- Persist account and balance data in a database
- Add PIN unblock / reset flow
- Add unit tests for `BankTool_v2`
- Use a proper command framework (e.g., `System.CommandLine`) instead of Regex
- Improve input validation and error handling

---

## License
Add a license file (example: MIT) as `LICENSE`.
