namespace Loans.Api.Tests.Integration;

public static class Wait
{
    /// <summary>Checks the condition every 100 ms until it's true. Fails after 30 seconds.</summary>
    public static async Task UntilAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(100);
        }
    }
}
