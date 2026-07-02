import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { History, Plus, AlertCircle, FileSpreadsheet } from 'lucide-react';
import api from '../services/api';

const Transactions: React.FC = () => {
  const queryClient = useQueryClient();
  const [showAddForm, setShowAddForm] = useState(false);

  // Form states
  const [holdingId, setHoldingId] = useState('');
  const [transactionType, setTransactionType] = useState('Buy');
  const [amount, setAmount] = useState('');
  const [quantity, setQuantity] = useState('');
  const [price, setPrice] = useState('');
  const [transactionDate, setTransactionDate] = useState('');

  // Fetch transactions
  const { data: transactions = [], isLoading } = useQuery({
    queryKey: ['transactionsList'],
    queryFn: async () => {
      const response = await api.get('/transactions');
      return response.data;
    }
  });

  // Fetch holdings (to select in form)
  const { data: holdings = [] } = useQuery({
    queryKey: ['holdingsListForTx'],
    queryFn: async () => {
      const response = await api.get('/holdings');
      return response.data;
    }
  });

  // Log transaction mutation
  const logTransactionMutation = useMutation({
    mutationFn: async (newTx: any) => {
      const response = await api.post('/transactions', newTx);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['transactionsList'] });
      queryClient.invalidateQueries({ queryKey: ['holdingsList'] });
      queryClient.invalidateQueries({ queryKey: ['dashboardSummary'] });
      setShowAddForm(false);
      resetForm();
    },
    onError: (err: any) => {
      alert(err.response?.data?.message || 'Failed to log transaction.');
    }
  });

  const resetForm = () => {
    setHoldingId('');
    setTransactionType('Buy');
    setAmount('');
    setQuantity('');
    setPrice('');
    setTransactionDate('');
  };

  const handleHoldingChange = (id: string) => {
    setHoldingId(id);
    const selected = holdings.find((h: any) => h.id === id);
    if (selected) {
      // Set reasonable default transaction types
      if (selected.assetType === 'Stock' || selected.assetType === 'ETF' || selected.assetType === 'MutualFund') {
        setTransactionType('Buy');
      } else if (selected.assetType === 'SavingsAccount' || selected.assetType === 'FixedDeposit') {
        setTransactionType('Deposit');
      }
    }
  };

  const selectedHolding = holdings.find((h: any) => h.id === holdingId);
  const isEquitySelected = selectedHolding && 
    (selectedHolding.assetType === 'Stock' || selectedHolding.assetType === 'ETF' || selectedHolding.assetType === 'MutualFund');

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();

    if (!holdingId) {
      alert('Please select a holding.');
      return;
    }

    const payload: any = {
      holdingId,
      transactionType,
      amount: parseFloat(amount)
    };

    if (isEquitySelected) {
      if ((selectedHolding.assetType === 'Stock' || selectedHolding.assetType === 'ETF') && !Number.isInteger(parseFloat(quantity))) {
        alert('Quantity for Stocks and ETFs must be a whole number (no fractional shares allowed in India).');
        return;
      }
      payload.quantityOrUnits = parseFloat(quantity);
      payload.priceOrNAV = parseFloat(price);
      // Auto-calculate amount if quantity & price are filled but amount is empty
      if (!amount && quantity && price) {
        payload.amount = parseFloat(quantity) * parseFloat(price);
      }
    }

    if (transactionDate) {
      payload.transactionDate = new Date(transactionDate).toISOString();
    }

    logTransactionMutation.mutate(payload);
  };

  return (
    <div className="space-y-8">
      {/* Header */}
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-3xl font-extrabold text-white tracking-tight">Audit Transactions</h1>
          <p className="text-gray-400 text-sm mt-1">Immutable ledger logs for all investment operations.</p>
        </div>
        
        <button
          onClick={() => setShowAddForm(!showAddForm)}
          className="flex items-center gap-2 bg-primary-600 hover:bg-primary-700 text-white px-4 py-2.5 rounded-lg text-sm font-semibold transition-colors"
        >
          <Plus className="w-4 h-4" />
          <span>Log Transaction</span>
        </button>
      </div>

      {/* Log Transaction Form */}
      {showAddForm && (
        <div className="bg-card border border-border rounded-xl p-6 shadow-xl space-y-6">
          <h3 className="text-base font-bold text-white flex items-center gap-2">
            <FileSpreadsheet className="w-5 h-5 text-primary-500" />
            <span>Enter Audit Transaction Details</span>
          </h3>

          <form onSubmit={handleSubmit} className="grid grid-cols-1 md:grid-cols-4 gap-6">
            {/* Target Asset */}
            <div>
              <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Target Asset</label>
              <select
                value={holdingId}
                onChange={(e) => handleHoldingChange(e.target.value)}
                className="w-full bg-[#0E1524] border border-border rounded-lg px-3 py-2.5 text-sm text-white focus:outline-none focus:border-primary-500"
                required
              >
                <option value="">-- Choose Asset --</option>
                {holdings.map((h: any) => (
                  <option key={h.id} value={h.id}>
                    {h.symbolOrName} ({h.assetType})
                  </option>
                ))}
              </select>
            </div>

            {/* Transaction Type */}
            <div>
              <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Transaction Type</label>
              <select
                value={transactionType}
                onChange={(e) => setTransactionType(e.target.value)}
                className="w-full bg-[#0E1524] border border-border rounded-lg px-3 py-2.5 text-sm text-white focus:outline-none focus:border-primary-500"
                required
              >
                {selectedHolding && (selectedHolding.assetType === 'Stock' || selectedHolding.assetType === 'ETF' || selectedHolding.assetType === 'MutualFund') ? (
                  <>
                    <option value="Buy">Buy (Add Shares)</option>
                    <option value="Sell">Sell (Reduce Shares)</option>
                    <option value="Dividend">Dividend (Cash Received)</option>
                  </>
                ) : (
                  <>
                    <option value="Deposit">Deposit</option>
                    <option value="Withdrawal">Withdrawal</option>
                    <option value="InterestCredit">Interest Credit</option>
                  </>
                )}
              </select>
            </div>

            {/* Quantity / Price (Equities only) */}
            {isEquitySelected && (
              <>
                <div>
                  <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Shares / Units</label>
                  <input
                    type="number"
                    step={selectedHolding?.assetType === 'MutualFund' ? "0.0001" : "1"}
                    value={quantity}
                    onChange={(e) => setQuantity(e.target.value)}
                    placeholder="0"
                    className="w-full bg-[#0E1524] border border-border rounded-lg px-3.5 py-2 text-sm text-white focus:outline-none focus:border-primary-500"
                    required
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Price Per Share</label>
                  <input
                    type="number"
                    step="0.01"
                    value={price}
                    onChange={(e) => setPrice(e.target.value)}
                    placeholder="₹0.00"
                    className="w-full bg-[#0E1524] border border-border rounded-lg px-3.5 py-2 text-sm text-white focus:outline-none focus:border-primary-500"
                    required
                  />
                </div>
              </>
            )}

            {/* Net Amount */}
            <div>
              <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">
                Net Cash Amount
              </label>
              <input
                type="number"
                step="0.01"
                value={amount}
                onChange={(e) => setAmount(e.target.value)}
                placeholder={isEquitySelected ? 'Optional (Auto computed)' : 'e.g. 500.00'}
                className="w-full bg-[#0E1524] border border-border rounded-lg px-3.5 py-2 text-sm text-white focus:outline-none focus:border-primary-500"
                required={!isEquitySelected}
              />
            </div>

            {/* Date */}
            <div>
              <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Transaction Date</label>
              <input
                type="date"
                value={transactionDate}
                onChange={(e) => setTransactionDate(e.target.value)}
                className="w-full bg-[#0E1524] border border-border rounded-lg px-3.5 py-2 text-sm text-white focus:outline-none focus:border-primary-500"
              />
            </div>

            {/* Submit */}
            <div className="md:col-span-4 flex justify-end gap-3 mt-2">
              <button
                type="button"
                onClick={() => setShowAddForm(false)}
                className="bg-transparent hover:bg-border text-gray-400 px-4 py-2 rounded-lg text-sm transition-colors"
              >
                Cancel
              </button>
              <button
                type="submit"
                disabled={logTransactionMutation.isPending}
                className="bg-primary-600 hover:bg-primary-700 text-white px-5 py-2 rounded-lg text-sm font-semibold transition-colors disabled:opacity-50"
              >
                {logTransactionMutation.isPending ? 'Logging...' : 'Record Transaction'}
              </button>
            </div>
          </form>
        </div>
      )}

      {/* Transaction Ledger Table */}
      <div className="bg-card border border-border rounded-xl overflow-hidden shadow-lg">
        {isLoading ? (
          <div className="p-12 text-center text-gray-400">Loading audit history...</div>
        ) : transactions.length > 0 ? (
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="border-b border-border bg-[#0E1524] text-xs font-bold text-gray-400 uppercase tracking-wider">
                  <th className="px-6 py-4">Transaction Date</th>
                  <th className="px-6 py-4">Asset</th>
                  <th className="px-6 py-4">Type</th>
                  <th className="px-6 py-4 text-right">Shares / Qty</th>
                  <th className="px-6 py-4 text-right">Price</th>
                  <th className="px-6 py-4 text-right">Net Value</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border text-sm">
                {transactions.map((t: any) => (
                  <tr key={t.id} className="hover:bg-[#121A2E]/40 transition-colors">
                    <td className="px-6 py-4 text-gray-400 font-mono">
                      {new Date(t.transactionDate).toLocaleDateString()}
                    </td>
                    <td className="px-6 py-4">
                      <div>
                        <span className="font-bold text-white block">{t.holdingName}</span>
                        <span className="text-xs text-gray-500 font-mono">{t.holdingAssetType}</span>
                      </div>
                    </td>
                    <td className="px-6 py-4">
                      <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold ${
                        t.transactionType === 'Buy' || t.transactionType === 'Deposit' ? 'bg-accent-500/10 text-accent-500' :
                        t.transactionType === 'Sell' || t.transactionType === 'Withdrawal' ? 'bg-danger-500/10 text-danger-500' :
                        'bg-warning-500/10 text-warning-500'
                      }`}>
                        {t.transactionType}
                      </span>
                    </td>
                    <td className="px-6 py-4 text-right font-mono text-gray-300">
                      {t.quantityOrUnits ? t.quantityOrUnits.toLocaleString(undefined, { minimumFractionDigits: 2 }) : '-'}
                    </td>
                    <td className="px-6 py-4 text-right font-mono text-gray-400">
                      {t.priceOrNAV ? `₹${t.priceOrNAV.toFixed(2)}` : '-'}
                    </td>
                    <td className={`px-6 py-4 text-right font-mono font-bold ${
                      t.transactionType === 'Buy' || t.transactionType === 'Deposit' ? 'text-accent-500' :
                      t.transactionType === 'Sell' || t.transactionType === 'Withdrawal' ? 'text-danger-500' : 'text-primary-500'
                    }`}>
                      {t.transactionType === 'Buy' || t.transactionType === 'Deposit' ? '+' : '-'}
                      ₹{t.amount?.toLocaleString(undefined, { minimumFractionDigits: 2 })}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="p-12 text-center text-gray-500 flex flex-col items-center gap-4">
            <History className="w-12 h-12 text-gray-600" />
            <div>
              <p className="font-bold text-white">No transactions recorded</p>
              <p className="text-sm mt-1">Audit log is empty. Transactions are populated when holdings are added.</p>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default Transactions;
