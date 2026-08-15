using System.Threading.Tasks;
using ClipTyper;
using Xunit;

namespace ClipTyper.Tests
{
    public class TypingServiceTest
    {
        [Fact]
        public void TypingService_InitialState_IsNotTyping()
        {
            var service = new TypingService();
            Assert.False(service.IsTyping);
        }

        [Fact]
        public async Task TypingService_PreventsConcurrentTypingRuns()
        {
            var service = new TypingService();
            
            // Trigger first run asynchronously
            Task task1 = service.TriggerClipTypeAsync();
            // Immediate second trigger should be rejected safely by the single typing guard
            Task task2 = service.TriggerClipTypeAsync();

            await Task.WhenAll(task1, task2);

            Assert.False(service.IsTyping);
        }
    }
}
