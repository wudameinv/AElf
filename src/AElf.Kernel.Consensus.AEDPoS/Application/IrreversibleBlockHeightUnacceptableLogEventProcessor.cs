using System.Threading.Tasks;
using AElf.Contracts.Consensus.AEDPoS;
using AElf.Kernel.SmartContract.Application;
using AElf.Kernel.Txn.Application;
using AElf.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AElf.CSharp.Core.Extension;
using Volo.Abp.Threading;

namespace AElf.Kernel.Consensus.AEDPoS.Application
{
    public class IrreversibleBlockHeightUnacceptableLogEventProcessor : LogEventProcessorBase,
        IBlocksExecutionSucceededLogEventProcessor
    {
        private readonly TransactionPackingOptions _transactionPackingOptions;
        private readonly ISmartContractAddressService _smartContractAddressService;
        
        public ILogger<IrreversibleBlockHeightUnacceptableLogEventProcessor> Logger { get; set; }

        public IrreversibleBlockHeightUnacceptableLogEventProcessor(
            IOptionsMonitor<TransactionPackingOptions> transactionPackingOptions,
            ISmartContractAddressService smartContractAddressService)
        {
            _transactionPackingOptions = transactionPackingOptions.CurrentValue;
            _smartContractAddressService = smartContractAddressService;

            Logger = NullLogger<IrreversibleBlockHeightUnacceptableLogEventProcessor>.Instance;
        }
        
        public override InterestedEvent GetInterestedEvent(IChainContext chainContext)
        {
            if (InterestedEvent != null)
                return InterestedEvent;
            var smartContractAddress = AsyncHelper.RunSync(() => _smartContractAddressService.GetSmartContractAddressAsync(
                chainContext, ConsensusSmartContractAddressNameProvider.Name));
            if (smartContractAddress == null) return null;
            var logEvent = new IrreversibleBlockHeightUnacceptable().ToLogEvent(smartContractAddress.Address);
            var interestedEvent = new InterestedEvent
            {
                LogEvent = logEvent,
                Bloom = logEvent.GetBloom()
            };
            if (!AsyncHelper.RunSync(() => _smartContractAddressService.CheckSmartContractAddressIrreversibleAsync(smartContractAddress)))
                return interestedEvent;
            InterestedEvent = interestedEvent;
            return InterestedEvent;
        }

        protected override Task ProcessLogEventAsync(Block block, LogEvent logEvent)
        {
            var distanceToLib = new IrreversibleBlockHeightUnacceptable();
            distanceToLib.MergeFrom(logEvent);

            if (distanceToLib.DistanceToIrreversibleBlockHeight > 0)
            {
                Logger.LogDebug($"Distance to lib height: {distanceToLib.DistanceToIrreversibleBlockHeight}");
                _transactionPackingOptions.IsTransactionPackable = false;
            }
            else
            {
                _transactionPackingOptions.IsTransactionPackable = true;
            }

            return Task.CompletedTask;
        }
    }
}