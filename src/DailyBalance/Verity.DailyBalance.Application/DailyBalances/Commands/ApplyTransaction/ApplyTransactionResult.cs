namespace Verity.DailyBalance.Application.DailyBalances.Commands.ApplyTransaction;

/// <summary><paramref name="WasAlreadyProcessed"/> indica uma reentrega deduplicada pela Inbox (no-op).</summary>
public sealed record ApplyTransactionResult(bool WasAlreadyProcessed);
