using System.Threading.Tasks;

namespace LedgerX.Core.Interfaces
{
    public interface IMessageBroker
    {
        Task PublishJobAsync<T>(string routingKey, T payload);
    }
}
