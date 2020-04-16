using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AElf.Types;

namespace AElf.Kernel.SmartContract.Application
{
    public interface ILogEventProcessor
    {
        InterestedEvent GetInterestedEvent(IChainContext chainContext);
        Task ProcessAsync(Block block, Dictionary<TransactionResult, List<LogEvent>> logEventsMap);
    }

    public class InterestedEvent
    {
        public LogEvent LogEvent { get; set; }
        public Bloom Bloom { get; set; }
    }

    public interface IBlockAcceptedLogEventProcessor : ILogEventProcessor
    {
    }

    public interface IBlocksExecutionSucceededLogEventProcessor : ILogEventProcessor
    {
    }

    public abstract class LogEventProcessorBase : ILogEventProcessor
    {
        protected InterestedEvent InterestedEvent;

        public abstract InterestedEvent GetInterestedEvent(IChainContext chainContext);

        public async Task ProcessAsync(Block block, Dictionary<TransactionResult, List<LogEvent>> logEventsMap)
        {
            foreach (var logEvent in logEventsMap.Values.SelectMany(logEvents => logEvents))
            {
                await ProcessLogEventAsync(block, logEvent);
            }
        }

        protected abstract Task ProcessLogEventAsync(Block block, LogEvent logEvent);
    }
}