namespace Verity.Ledger.Application.Abstractions;

/// <summary>Isola o algoritmo de hashing de senha (implementado na Infrastructure) do caso de uso.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}
