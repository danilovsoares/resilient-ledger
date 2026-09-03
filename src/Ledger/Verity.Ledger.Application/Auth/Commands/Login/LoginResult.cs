namespace Verity.Ledger.Application.Auth.Commands.Login;

public sealed record LoginResult(Guid UserId, string Username, string DisplayName);
