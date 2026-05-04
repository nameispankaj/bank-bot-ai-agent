# Bank Bot (C#) – Mini Banking + Azure AI Console Assistant

Bank Bot is a C# console mini-project that supports **banking operations** (login, balance, deposit, withdraw, logout) and uses **Azure AI Projects** to answer any non-banking prompts.

It also supports natural language such as:
- “how much money i have?”
- “top up to 2000”
- “if my balance is below 2000 deposit money and make it 2000”

---

## Features

### Banking (rule-based + safe execution)
- Login with demo credentials
- Check balance (supports natural language)
- Deposit/Add money (requires UPI PIN)
- Withdraw money (requires UPI PIN)
- Logout
- UPI PIN rules:
  - Must be exactly **4 digits**
  - **3 tries** allowed
  - After 3 wrong PIN attempts, transactions are blocked

### Smart “Top-up to target” behavior
Understands requests like:
- “top up to 2000”
- “if my balance is below 2000 make it 2000”
- “if balance is less than 2000 deposit and make it 2000”

It deposits only the **difference** needed to reach the target amount.

### AI fallback (Azure AI Agent)
If the input is not recognized as a banking request, the message is sent to the Azure AI agent (configured endpoint + model) and the response is shown.

---

## Supported Commands

- `login <username> <password>`
- `balance`
- `deposit/add <amount> [pin <1234>]`
- `withdraw <amount> [pin <1234>]`
- `logout`
- `exit`

---

## Natural Language Examples

```text
You: how much money i have?
Bank Bot: Not logged in. Please login first.

You: login admin Admin@123
Bank Bot: Login successful.

You: how much money i have?
Bank Bot: Current balance: 1000.

You: if my balance is below 2000 deposit money and make it 2000
UPI PIN (4 digits): 1234
Bank Bot: Amount added successfully. New balance: 2000.
```

---

## Logic (How Bank Bot Works)

This project uses a **hybrid approach**:

### 1) Input loop
- The console reads one line at a time.
- `exit` stops the program.
- Everything else is processed as either:
  - a **banking request**, or
  - a **general prompt** (sent to Azure AI).

### 2) Banking command detection (Regex + simple natural language)
The app tries to match the user’s message against known banking intents:

**A) Login**
- Pattern: `login <username> <password>`
- Tracks failed attempts (`_loginAttempts`)
- After 3 failed attempts, login is denied.

**B) Balance**
- Detects words like: `balance`, `money`, `funds`
- Detects question-like words like: `how much`, `what`, `show`, `tell`, `current`
- Calls `CheckBalance()` (requires login).

**C) Deposit**
- Detects deposit verbs: `deposit`, `add`, `credit`, `top up`, `put in`
- Extracts the first positive integer as the amount (example: 500)
- PIN handling:
  - If PIN is present in the text (`pin 1234`, `upi 1234`), it uses it
  - Otherwise it prompts in console for the PIN

**D) Withdraw**
- Detects withdraw verbs: `withdraw`, `take out`, `debit`, `remove`
- Extracts the first positive integer as the amount
- PIN handling same as deposit

### 3) “Top-up to target” logic (fix for “make it 2000”)
When the user asks to make the balance reach a target **only if below** a value (example: 2000), the bot:
1. Reads current balance (requires login)
2. If `current >= target`: no deposit is performed
3. If `current < target`:
   - Calculates: `depositNeeded = target - current`
   - Deposits exactly `depositNeeded`

This prevents incorrect behavior like depositing the full target amount (which would overshoot).

### 4) UPI PIN security logic
For deposit/withdraw:
- PIN must be **exactly 4 digits**
- If PIN is wrong:
  - tries reduce from 3 → 2 → 1 → 0
  - after 0, transactions are blocked (`_transactionsBlocked = true`)
- On correct PIN, tries reset back to 3

### 5) AI fallback logic
If the app cannot confidently match a banking intent, it forwards the message to Azure AI:
- `agent.RunAsync(userText, session)`
This is used for:
- general questions
- prompts not related to banking actions

---

## Configuration (No hardcoded endpoints or keys)

This project **does not hardcode API keys or endpoints** in source code.

It uses:
- `AzureCliCredential()` for authentication (no key in code)
- Environment variables for configuration

### Required environment variable
- `AZURE_AI_PROJECT_ENDPOINT`

Example format:
`https://<your-resource>.services.ai.azure.com/api/projects/<your-project>`

### Optional environment variable
- `AZURE_AI_MODEL`  
Default: `gpt-4.1-nano`

---

## Setup & Run

### 1) Prerequisites
- .NET SDK (recommended: .NET 8+)
- Azure CLI installed
- Access to an Azure AI Project

### 2) Login to Azure (required for AzureCliCredential)
```bash
az login
```

### 3) Set environment variables

#### macOS / Linux (bash/zsh)
```bash
export AZURE_AI_PROJECT_ENDPOINT="https://<your-resource>.services.ai.azure.com/api/projects/<your-project>"
export AZURE_AI_MODEL="gpt-4.1-nano"
```

#### Windows PowerShell
```powershell
$env:AZURE_AI_PROJECT_ENDPOINT="https://<your-resource>.services.ai.azure.com/api/projects/<your-project>"
$env:AZURE_AI_MODEL="gpt-4.1-nano"
```

### 4) Run
```bash
dotnet restore
dotnet run
```

---

## Security Notes (Important)

This is a mini-project/demo.

- Demo banking credentials and UPI PIN are hardcoded for learning:
  - Username: `admin`
  - Password: `Admin@123`
  - UPI PIN: `1234`
- Balance is stored in memory and resets each run.
- Do not use this code for real banking/production systems.

---

## Troubleshooting

### Missing `AZURE_AI_PROJECT_ENDPOINT`
You will see:
- `Bank Bot: Missing environment variable AZURE_AI_PROJECT_ENDPOINT`

Fix: set the environment variable and re-run.

### Azure authentication issues
- Run `az login`
- Check account:
  ```bash
  az account show
  ```

---

## Suggested Repo Names
- `bank-bot-azure-ai`
- `ai-bank-bot`
- `bank-bot-console`

---

## License
Add your preferred license (example: MIT) as `LICENSE`.
