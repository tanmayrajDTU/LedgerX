using System.Threading.Tasks;

namespace LedgerX.Core.Interfaces
{
    public interface IMarketDataProvider
    {
        Task<decimal> GetLatestPriceAsync(string symbolOrName, string assetType, bool useLiveData, bool injectFault = false);
    }
}
