using System;

namespace WebPowerShell.Application.UnitTests
{
    public class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

        public FakeTimeProvider()
        {
        }

        public FakeTimeProvider(DateTimeOffset startUtc)
        {
            _utcNow = startUtc;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public void Advance(TimeSpan amount)
        {
            _utcNow = _utcNow.Add(amount);
        }
    }
}
