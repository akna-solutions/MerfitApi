namespace Merfit.Application.Common.Interfaces;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <returns>true if the password matches; the out parameter signals the hash should be upgraded (algorithm/iteration count changed) and re-persisted.</returns>
    bool Verify(string hashedPassword, string providedPassword, out bool needsRehash);
}
