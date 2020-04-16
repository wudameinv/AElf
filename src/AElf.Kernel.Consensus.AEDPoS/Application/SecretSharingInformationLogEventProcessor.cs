using System.Threading.Tasks;
using AElf.Contracts.Consensus.AEDPoS;
using AElf.Kernel.SmartContract.Application;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using Volo.Abp.Threading;

namespace AElf.Kernel.Consensus.AEDPoS.Application
{
    internal class SecretSharingInformationLogEventProcessor : LogEventProcessorBase, IBlocksExecutionSucceededLogEventProcessor
    {
        private readonly ISmartContractAddressService _smartContractAddressService;
        private readonly ISecretSharingService _secretSharingService;

        public SecretSharingInformationLogEventProcessor(
            ISmartContractAddressService smartContractAddressService,
            ISecretSharingService secretSharingService)
        {
            _smartContractAddressService = smartContractAddressService;
            _secretSharingService = secretSharingService;
        }

        public override InterestedEvent GetInterestedEvent(IChainContext chainContext)
        {
            if (InterestedEvent != null) return InterestedEvent;
            var smartContractAddress = AsyncHelper.RunSync(() => _smartContractAddressService.GetSmartContractAddressAsync(
                chainContext, ConsensusSmartContractAddressNameProvider.Name));
            if (smartContractAddress == null) return null;
            var logEvent = new SecretSharingInformation().ToLogEvent(smartContractAddress.Address);
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

        protected override async Task ProcessLogEventAsync(Block block, LogEvent logEvent)
        {
            var secretSharingInformation = new SecretSharingInformation();
            secretSharingInformation.MergeFrom(logEvent);
            await _secretSharingService.AddSharingInformationAsync(secretSharingInformation);
        }
    }
}