using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core.Extension;
using AElf.Kernel.SmartContract.Application;
using AElf.Kernel.Token;
using AElf.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.Threading;

namespace AElf.Kernel.SmartContract.ExecutionPluginForResourceFee
{
    public class ResourceTokenChargedLogEventProcessor : IBlockAcceptedLogEventProcessor
    {
        private readonly ISmartContractAddressService _smartContractAddressService;
        private readonly ITotalResourceTokensMapsProvider _totalTotalResourceTokensMapsProvider;
        private InterestedEvent _interestedEvent;
        private ILogger<ResourceTokenChargedLogEventProcessor> Logger { get; set; }

        public ResourceTokenChargedLogEventProcessor(ISmartContractAddressService smartContractAddressService,
            ITotalResourceTokensMapsProvider totalTotalResourceTokensMapsProvider)
        {
            _smartContractAddressService = smartContractAddressService;
            _totalTotalResourceTokensMapsProvider = totalTotalResourceTokensMapsProvider;
            Logger = NullLogger<ResourceTokenChargedLogEventProcessor>.Instance;
        }

        public InterestedEvent GetInterestedEvent(IChainContext chainContext)
        {
            if (_interestedEvent != null)
                return _interestedEvent;

            var smartContractAddress = AsyncHelper.RunSync(() => _smartContractAddressService.GetSmartContractAddressAsync(
                chainContext, TokenSmartContractAddressNameProvider.Name));

            if (smartContractAddress == null) return null;
            var logEvent = new ResourceTokenCharged().ToLogEvent(smartContractAddress.Address);
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
            var blockHash = block.GetHash();
            var blockHeight = block.Height;
            var totalResourceTokensMaps = new TotalResourceTokensMaps
            {
                BlockHash = blockHash,
                BlockHeight = blockHeight
            };

            foreach (var logEvent in logEventsMap.Values.SelectMany(logEvents => logEvents))
            {
                var eventData = new ResourceTokenCharged();
                eventData.MergeFrom(logEvent);
                if (eventData.Symbol == null || eventData.Amount == 0)
                    continue;

                if (totalResourceTokensMaps.Value.Any() &&
                    totalResourceTokensMaps.Value.Any(b => b.ContractAddress == eventData.ContractAddress))
                {
                    var oldBill =
                        totalResourceTokensMaps.Value.First(b => b.ContractAddress == eventData.ContractAddress);
                    if (oldBill.TokensMap.Value.ContainsKey(eventData.Symbol))
                    {
                        oldBill.TokensMap.Value[eventData.Symbol] += eventData.Amount;
                    }
                    else
                    {
                        oldBill.TokensMap.Value.Add(eventData.Symbol, eventData.Amount);
                    }
                }
                else
                {
                    var contractTotalResourceTokens = new ContractTotalResourceTokens
                    {
                        ContractAddress = eventData.ContractAddress,
                        TokensMap = new TotalResourceTokensMap
                        {
                            Value =
                            {
                                {eventData.Symbol, eventData.Amount}
                            }
                        }
                    };
                    totalResourceTokensMaps.Value.Add(contractTotalResourceTokens);
                }
            }

            if (totalResourceTokensMaps.Value.Any())
            {
                await _totalTotalResourceTokensMapsProvider.SetTotalResourceTokensMapsAsync(new BlockIndex
                {
                    BlockHash = blockHash,
                    BlockHeight = blockHeight
                }, totalResourceTokensMaps);
            }
        }
    }
}