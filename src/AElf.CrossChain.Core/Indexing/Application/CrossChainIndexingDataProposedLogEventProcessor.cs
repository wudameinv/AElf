using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Acs3;
using AElf.Contracts.CrossChain;
using AElf.Kernel;
using AElf.Kernel.SmartContract.Application;
using AElf.CSharp.Core.Extension;
using AElf.Kernel.Proposal.Application;
using AElf.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Threading;

namespace AElf.CrossChain.Indexing.Application
{
    public class CrossChainIndexingDataProposedLogEventProcessor : IBlocksExecutionSucceededLogEventProcessor
    {
        public IOptionsMonitor<CrossChainConfigOptions> CrossChainConfigOptions { get; set; }
        public ILogger<CrossChainIndexingDataProposedLogEventProcessor> Logger { get; set; }

        private readonly ISmartContractAddressService _smartContractAddressService;
        private readonly ICrossChainIndexingDataValidationService _crossChainIndexingDataValidationService;
        private readonly IProposalService _proposalService;
        private InterestedEvent _interestedEvent;

        public CrossChainIndexingDataProposedLogEventProcessor(ISmartContractAddressService smartContractAddressService,
            ICrossChainIndexingDataValidationService crossChainIndexingDataValidationService,
            IProposalService proposalService)
        {
            _smartContractAddressService = smartContractAddressService;
            _crossChainIndexingDataValidationService = crossChainIndexingDataValidationService;
            _proposalService = proposalService;
        }
        
        public InterestedEvent GetInterestedEvent(IChainContext chainContext)
        {
            if (_interestedEvent != null)
                return _interestedEvent;

            var smartContractAddress = AsyncHelper.RunSync(() => _smartContractAddressService.GetSmartContractAddressAsync(
                chainContext, CrossChainSmartContractAddressNameProvider.Name));
            if (smartContractAddress == null) return null;

            var logEvent = new CrossChainIndexingDataProposedEvent().ToLogEvent(smartContractAddress.Address);
            var interestedEvent = new InterestedEvent
            {
                LogEvent = logEvent,
                Bloom = logEvent.GetBloom()
            };
            if (!AsyncHelper.RunSync(() => _smartContractAddressService.CheckSmartContractAddressIrreversibleAsync(smartContractAddress)))
                return interestedEvent;
            _interestedEvent = interestedEvent;
            
            return _interestedEvent;
        }

        public async Task ProcessAsync(Block block, Dictionary<TransactionResult, List<LogEvent>> logEventsMap)
        {
            foreach (var events in logEventsMap)
            {
                var transactionResult = events.Key;
                foreach (var logEvent in events.Value)
                {
                    if (CrossChainConfigOptions.CurrentValue.CrossChainDataValidationIgnored)
                    {
                        Logger.LogTrace("Cross chain data validation disabled.");
                        return;
                    }

                    var crossChainIndexingDataProposedEvent = new CrossChainIndexingDataProposedEvent();
                    crossChainIndexingDataProposedEvent.MergeFrom(logEvent);
                    var crossChainBlockData = crossChainIndexingDataProposedEvent.ProposedCrossChainData;
                    if (crossChainBlockData.IsNullOrEmpty())
                        return;
                    var validationResult =
                        await _crossChainIndexingDataValidationService.ValidateCrossChainIndexingDataAsync(
                            crossChainBlockData,
                            block.GetHash(), block.Height);
                    if (validationResult)
                    {
                        Logger.LogDebug(
                            $"Valid cross chain indexing proposal found, block height {block.Height}, block hash {block.GetHash()} ");
                        var proposalId = ProposalCreated.Parser
                            .ParseFrom(transactionResult.Logs.First(l => l.Name == nameof(ProposalCreated)).NonIndexed)
                            .ProposalId;
                        _proposalService.AddNotApprovedProposal(proposalId, block.Height);
                    }
                }
            }
        }
    }
}