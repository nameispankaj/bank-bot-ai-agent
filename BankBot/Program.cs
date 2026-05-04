using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;

public sealed class BankTool_v2
{
    private const string CorrectUsername = "admin";
    private const string CorrectPassword = "Admin@123";

    // UPI PIN rules
    private const string CorrectUpiPin = "1234";
    private int _upiTriesLeft = 3;
    private bool _transactionsBlocked = false;

    // Login rules
    private int _loginAttempts = 0;
    private bool _isLoggedIn = false;

    private int _balance = 1000;

    public string Login(string username, string password)
    {
        if (_isLoggedIn) return "Already logged in.";
        if (_loginAttempts >= 3) return "Access denied. No login attempts left.";

        if (username == CorrectUsername && password == CorrectPassword)
        {
            _isLoggedIn = true;
            return "Login successful.";
        }

        _loginAttempts++;
        if (_loginAttempts >= 3) return "Invalid credentials. Access denied.";
        return $"Invalid credentials. Attempts left: {3 - _loginAttempts}.";
    }

    public string CheckBalance()
    {
        EnsureLoggedIn();
        return $"Current balance: {_balance}.";
    }

    // Helpful for logic like "top up to X"
    public int GetBalanceValueForLogic()
    {
        EnsureLoggedIn();
        return _balance;
    }

    public string AddMoney(int amount, string upiPin)
    {
        EnsureLoggedIn();
        EnsureTransactionsNotBlocked();
        ValidateUpiPinOrThrow(upiPin);

        if (amount <= 0) return "Invalid amount. Must be > 0.";
        _balance += amount;
        return $"Amount added successfully. New balance: {_balance}.";
    }

    public string WithdrawMoney(int amount, string upiPin)
    {
        EnsureLoggedIn();
        EnsureTransactionsNotBlocked();
        ValidateUpiPinOrThrow(upiPin);

        if (amount <= 0) return "Invalid amount. Must be > 0.";
        if (amount > _balance) return "Insufficient balance.";
        _balance -= amount;
        return $"Withdrawal successful. New balance: {_balance}.";
    }

    public string Logout()
    {
        _isLoggedIn = false;
        return "Logged out.";
    }

    private void EnsureLoggedIn()
    {
        if (!_isLoggedIn)
            throw new InvalidOperationException("Not logged in. Please login first.");
    }

    private void EnsureTransactionsNotBlocked()
    {
        if (_transactionsBlocked)
            throw new InvalidOperationException("Account is blocked for transactions due to failed UPI PIN attempts.");
    }

    private void ValidateUpiPinOrThrow(string upiPin)
    {
        if (!Regex.IsMatch(upiPin ?? "", @"^\d{4}$"))
            throw new InvalidOperationException("UPI PIN must be exactly 4 digits.");

        if (upiPin == CorrectUpiPin)
        {
            _upiTriesLeft = 3;
            return;
        }

        _upiTriesLeft--;
        if (_upiTriesLeft <= 0)
        {
            _transactionsBlocked = true;
            throw new InvalidOperationException("UPI PIN incorrect. Account blocked for transactions.");
        }

        throw new InvalidOperationException($"UPI PIN incorrect. Tries left: {_upiTriesLeft}.");
    }
}

public class Program
{
    public static async Task Main()
    {
        var bank = new BankTool_v2();

        // Read configuration from environment variables (no hardcoded endpoints/keys)
        // Required:
        //   AZURE_AI_PROJECT_ENDPOINT = https://<resource>.services.ai.azure.com/api/projects/<project>
        // Optional:
        //   AZURE_AI_MODEL = gpt-4.1-nano
        var endpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Console.WriteLine("Bank Bot: Missing environment variable AZURE_AI_PROJECT_ENDPOINT");
            Console.WriteLine("Bank Bot: Example:");
            Console.WriteLine("Bank Bot:   export AZURE_AI_PROJECT_ENDPOINT=\"https://<resource>.services.ai.azure.com/api/projects/<project>\"");
            return;
        }

        var model = Environment.GetEnvironmentVariable("AZURE_AI_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = "gpt-4.1-nano";

        // Uses AzureCliCredential => no API keys in code.
        // Make sure you ran: az login
        AIAgent agent = new AIProjectClient(
                new Uri(endpoint),
                new AzureCliCredential())
            .AsAIAgent(
                model: model,
                instructions: """
                You are Bank Bot. Keep answers brief.

                Supported commands:
                - login <username> <password>
                - balance
                - deposit/add <amount> [pin <1234>]
                - withdraw <amount> [pin <1234>]
                - logout
                - exit
                """);

        AgentSession session = await agent.CreateSessionAsync();

        while (true)
        {
            Console.Write("You: ");
            var youRaw = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(youRaw))
                continue;

            if (youRaw.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            var you = Regex.Replace(youRaw, @"^\s*You:\s*", "", RegexOptions.IgnoreCase);

            if (TryHandleBankingNatural(you, bank, out var bankReply))
            {
                Console.WriteLine($"Bank Bot: {bankReply}");
                continue;
            }

            var response = await agent.RunAsync(you, session);
            Console.WriteLine($"Bank Bot: {response}");
        }
    }

    private static bool TryHandleBankingNatural(string input, BankTool_v2 bank, out string reply)
    {
        reply = "";
        var text = input.Trim();

        // login (support incomplete "login")
        var loginMatch = Regex.Match(text, @"^\s*login(?:\s+(\S+)\s+(\S+))?\s*$", RegexOptions.IgnoreCase);
        if (loginMatch.Success)
        {
            var u = loginMatch.Groups[1].Value;
            var p = loginMatch.Groups[2].Value;

            if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p))
            {
                reply = "Usage: login <username> <password>";
                return true;
            }

            reply = bank.Login(u, p);
            return true;
        }

        // logout
        if (Regex.IsMatch(text, @"^\s*logout\s*$", RegexOptions.IgnoreCase))
        {
            reply = bank.Logout();
            return true;
        }

        // Natural-language "top up to target if below target"
        if (TryParseTopUpToTarget(text, out var targetAmount))
        {
            try
            {
                var current = bank.GetBalanceValueForLogic();

                if (current >= targetAmount)
                {
                    reply = $"Your balance is already {current}, which is not below {targetAmount}. No deposit needed.";
                    return true;
                }

                var depositNeeded = targetAmount - current;

                var pin = ExtractPinIfPresent(text);
                if (pin is null)
                {
                    Console.Write("UPI PIN (4 digits): ");
                    pin = Console.ReadLine()?.Trim();
                }

                reply = bank.AddMoney(depositNeeded, pin!);
                return true;
            }
            catch (Exception ex)
            {
                reply = ex.Message;
                return true;
            }
        }

        // Balance: natural language
        if (Regex.IsMatch(text, @"\b(balance|amount|money|funds)\b", RegexOptions.IgnoreCase) &&
            Regex.IsMatch(text, @"\b(how\s*much|what|check|show|tell|current)\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(text, @"^\s*balance\s*$", RegexOptions.IgnoreCase))
        {
            try { reply = bank.CheckBalance(); }
            catch (Exception ex) { reply = ex.Message; }
            return true;
        }

        // Deposit/add: natural language
        if (Regex.IsMatch(text, @"\b(add|deposit|put\s+in|credit|top\s*up|recharge)\b", RegexOptions.IgnoreCase))
        {
            if (!TryExtractFirstPositiveInt(text, out var amount))
            {
                reply = "Tell me the amount to add (example: deposit 500).";
                return true;
            }

            var pin = ExtractPinIfPresent(text);
            if (pin is null)
            {
                Console.Write("UPI PIN (4 digits): ");
                pin = Console.ReadLine()?.Trim();
            }

            try { reply = bank.AddMoney(amount, pin!); }
            catch (Exception ex) { reply = ex.Message; }
            return true;
        }

        // Withdraw: natural language
        if (Regex.IsMatch(text, @"\b(withdraw|withdrow|take\s*out|debit|remove)\b", RegexOptions.IgnoreCase))
        {
            if (!TryExtractFirstPositiveInt(text, out var amount))
            {
                reply = "Tell me the amount to withdraw (example: withdraw 200).";
                return true;
            }

            var pin = ExtractPinIfPresent(text);
            if (pin is null)
            {
                Console.Write("UPI PIN (4 digits): ");
                pin = Console.ReadLine()?.Trim();
            }

            try { reply = bank.WithdrawMoney(amount, pin!); }
            catch (Exception ex) { reply = ex.Message; }
            return true;
        }

        // If user explicitly tries to "set/make balance = X" without "top up" semantics, reject
        if (Regex.IsMatch(text, @"\b(set|make|change|update)\b.*\bbalance\b", RegexOptions.IgnoreCase))
        {
            reply = "I can’t set balance directly. I can only deposit or withdraw. Try: deposit <amount> or withdraw <amount>.";
            return true;
        }

        return false;
    }

    private static bool TryParseTopUpToTarget(string text, out int targetAmount)
    {
        targetAmount = 0;

        // Pattern 1: "top up to 2000" / "bring it to 2000" / "make it 2000"
        var p1 = Regex.Match(text, @"\b(top\s*up\s*to|bring\s*(it)?\s*to|make\s*(it)?\s*)\s*(\d+)\b", RegexOptions.IgnoreCase);
        if (p1.Success && int.TryParse(p1.Groups[3].Value, out targetAmount) && targetAmount > 0)
            return true;

        // Pattern 2: "if balance is below 2000 ... make it 2000"
        if (Regex.IsMatch(text, @"\b(if\b.*\bbalance\b.*\b(below|less\s+than|under)\b)", RegexOptions.IgnoreCase) &&
            Regex.IsMatch(text, @"\b(make|bring|top\s*up)\b", RegexOptions.IgnoreCase))
        {
            var allNums = Regex.Matches(text, @"\b(\d+)\b");
            if (allNums.Count > 0)
            {
                var last = allNums[allNums.Count - 1].Groups[1].Value;
                if (int.TryParse(last, out targetAmount) && targetAmount > 0)
                    return true;
            }
        }

        return false;
    }

    private static bool TryExtractFirstPositiveInt(string text, out int value)
    {
        value = 0;
        var m = Regex.Match(text, @"(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out value) && value > 0;
    }

    // Supports: "pin 1234" OR "upi 1234" OR "upi pin 1234"
    private static string? ExtractPinIfPresent(string text)
    {
        var m = Regex.Match(text, @"\b(?:upi\s*pin|pin|upi)\s*(\d{4})\b", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
}
