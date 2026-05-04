using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;

public sealed class BankTool_v2
{
    // Demo credentials (mini-project only)
    private const string CorrectUsername = "admin";
    private const string CorrectPassword = "Admin@123";

    // Demo UPI PIN rules
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
            // Reset tries after a success:
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

        // Load Azure AI configuration from environment (no hardcoding)
        var endpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Console.WriteLine("Bank Bot: Missing environment variable: AZURE_AI_PROJECT_ENDPOINT");
            Console.WriteLine("Bank Bot: Set it to: https://<your-resource>.services.ai.azure.com/api/projects/<your-project>");
            return;
        }

        var model = Environment.GetEnvironmentVariable("AZURE_AI_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = "gpt-4.1-nano";

        AIAgent agent = new AIProjectClient(
                new Uri(endpoint),
                new AzureCliCredential())
            .AsAIAgent(
                model: model,
                instructions: """
                You are Bank Bot. Keep answers brief.
                Commands supported:
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

            if (TryHandleBanking(you, bank, out var bankReply))
            {
                Console.WriteLine($"Bank Bot: {bankReply}");
                continue;
            }

            var response = await agent.RunAsync(you, session);
            Console.WriteLine($"Bank Bot: {response}");
        }
    }

    private static bool TryHandleBanking(string input, BankTool_v2 bank, out string reply)
    {
        reply = "";
        var text = input.Trim();

        // login <u> <p>
        var loginMatch = Regex.Match(text, @"^\s*login\s+(\S+)\s+(\S+)\s*$", RegexOptions.IgnoreCase);
        if (loginMatch.Success)
        {
            reply = bank.Login(loginMatch.Groups[1].Value, loginMatch.Groups[2].Value);
            return true;
        }

        // logout
        if (Regex.IsMatch(text, @"^\s*logout\s*$", RegexOptions.IgnoreCase))
        {
            reply = bank.Logout();
            return true;
        }

        // balance or "what is my balance"
        if (Regex.IsMatch(text, @"\bbalance\b", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(text, @"\b(add|deposit|withdraw|withdrow)\b", RegexOptions.IgnoreCase))
        {
            try { reply = bank.CheckBalance(); }
            catch (Exception ex) { reply = ex.Message; }
            return true;
        }

        // deposit/add (natural language too)
        if (Regex.IsMatch(text, @"\b(add|deposit)\b", RegexOptions.IgnoreCase))
        {
            if (!TryExtractFirstInt(text, out var amount))
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

        // withdraw/withdrow
        if (Regex.IsMatch(text, @"\b(withdraw|withdrow)\b", RegexOptions.IgnoreCase))
        {
            if (!TryExtractFirstInt(text, out var amount))
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

        return false;
    }

    private static bool TryExtractFirstInt(string text, out int value)
    {
        value = 0;
        var m = Regex.Match(text, @"(-?\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out value);
    }

    // Supports: "pin 1234" OR "upi 1234" OR "upi pin 1234"
    private static string? ExtractPinIfPresent(string text)
    {
        var m = Regex.Match(text, @"\b(?:upi\s*pin|pin|upi)\s*(\d{4})\b", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
}
