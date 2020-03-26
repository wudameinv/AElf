using System.Threading.Tasks;
using AElf.Contracts.MultiToken;
using AElf.Kernel.SmartContract.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.DependencyInjection;

namespace AElf.Kernel.SmartContract.ExecutionPluginForMethodFee
{
    public interface ITotalTransactionFeesMapProvider
    {
        Task<TotalTransactionFeesMap> GetTotalTransactionFeesMapAsync(IChainContext chainContext);
        Task SetTotalTransactionFeesMapAsync(IBlockIndex blockIndex, TotalTransactionFeesMap totalTransactionFeesMap);
    }

    public class TotalTransactionFeesMapProvider : BlockExecutedDataBaseProvider<TotalTransactionFeesMap>,
        ITotalTransactionFeesMapProvider, ISingletonDependency
    {
        private const string BlockExecutedDataName = nameof(TotalTransactionFeesMap);

        public ILogger<TotalTransactionFeesMapProvider> Logger { get; set; }
        public TotalTransactionFeesMapProvider(
            ICachedBlockchainExecutedDataService<TotalTransactionFeesMap> cachedBlockchainExecutedDataService) : base(
            cachedBlockchainExecutedDataService)
        {
            Logger = NullLogger<TotalTransactionFeesMapProvider>.Instance;
        }

        public Task<TotalTransactionFeesMap> GetTotalTransactionFeesMapAsync(IChainContext chainContext)
        {
            var totalTxFeesMap = GetBlockExecutedData(chainContext);
            return Task.FromResult(totalTxFeesMap);
        }

        public async Task SetTotalTransactionFeesMapAsync(IBlockIndex blockIndex,
            TotalTransactionFeesMap totalTransactionFeesMap)
        {
            Logger.LogInformation($"SetTotalTransactionFeesMapAsync: {totalTransactionFeesMap}");
            await AddBlockExecutedDataAsync(blockIndex, totalTransactionFeesMap);
        }

        protected override string GetBlockExecutedDataName()
        {
            return BlockExecutedDataName;
        }
    }
}