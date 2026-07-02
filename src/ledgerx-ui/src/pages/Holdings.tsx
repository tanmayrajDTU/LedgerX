import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Plus, Briefcase, Trash2, ShieldAlert, Sparkles } from 'lucide-react';
import api from '../services/api';

const Holdings: React.FC = () => {
  const queryClient = useQueryClient();
  const [showAddForm, setShowAddForm] = useState(false);

  // Form states
  const [assetType, setAssetType] = useState('Stock');
  const [symbolOrName, setSymbolOrName] = useState('');
  const [quantity, setQuantity] = useState('');
  const [buyPrice, setBuyPrice] = useState('');
  const [principal, setPrincipal] = useState('');
  const [interestRate, setInterestRate] = useState('');
  const [startDate, setStartDate] = useState('');
  const [maturityDate, setMaturityDate] = useState('');
  const [balance, setBalance] = useState('');

  // Autocomplete states
  const [searchResults, setSearchResults] = useState<any[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [showSuggestions, setShowSuggestions] = useState(false);

  // Fetch holdings
  const { data: holdings = [], isLoading } = useQuery({
    queryKey: ['holdingsList'],
    queryFn: async () => {
      const response = await api.get('/holdings');
      return response.data;
    }
  });

  // Create holding mutation
  const createHoldingMutation = useMutation({
    mutationFn: async (newHolding: any) => {
      const response = await api.post('/holdings', newHolding);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['holdingsList'] });
      queryClient.invalidateQueries({ queryKey: ['dashboardSummary'] });
      setShowAddForm(false);
      resetForm();
    },
    onError: (err: any) => {
      alert(err.response?.data?.message || 'Failed to add holding.');
    }
  });

  const resetForm = () => {
    setSymbolOrName('');
    setQuantity('');
    setBuyPrice('');
    setPrincipal('');
    setInterestRate('');
    setStartDate('');
    setMaturityDate('');
    setBalance('');
    setSearchResults([]);
    setShowSuggestions(false);
  };

  const handleSymbolChange = async (val: string) => {
    setSymbolOrName(val);
    if (['Stock', 'ETF', 'MutualFund'].includes(assetType) && val.trim().length >= 2) {
      setIsSearching(true);
      try {
        const response = await api.get(`/search?q=${encodeURIComponent(val)}`);
        const filtered = response.data.filter((item: any) => item.assetType === assetType);
        setSearchResults(filtered);
        setShowSuggestions(true);
      } catch (err) {
        console.error('Search failed', err);
      } finally {
        setIsSearching(false);
      }
    } else {
      setSearchResults([]);
      setShowSuggestions(false);
    }
  };

  const handleSelectSuggestion = (item: any) => {
    setSymbolOrName(item.symbol);
    setSearchResults([]);
    setShowSuggestions(false);
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();

    if (['Stock', 'ETF'].includes(assetType)) {
      if (!Number.isInteger(parseFloat(quantity))) {
        alert('Quantity for Stocks and ETFs must be a whole number (no fractional shares allowed in India).');
        return;
      }
    }

    const payload: any = {
      assetType,
      symbolOrName,
    };

    if (assetType === 'Stock' || assetType === 'ETF' || assetType === 'MutualFund') {
      payload.quantityOrUnits = parseFloat(quantity);
      payload.buyPriceOrNAV = parseFloat(buyPrice);
    } else if (assetType === 'FixedDeposit') {
      payload.principal = parseFloat(principal);
      payload.interestRate = parseFloat(interestRate);
      payload.startDate = startDate ? new Date(startDate).toISOString() : null;
      payload.maturityDate = maturityDate ? new Date(maturityDate).toISOString() : null;
    } else if (assetType === 'SavingsAccount') {
      payload.balance = parseFloat(balance);
    }

    createHoldingMutation.mutate(payload);
  };

  return (
    <div className="space-y-8">
      {/* Header */}
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-3xl font-extrabold text-white tracking-tight">Portfolio Holdings</h1>
          <p className="text-gray-400 text-sm mt-1">Track asset types, costs, current values, and earnings.</p>
        </div>
        
        <button
          onClick={() => setShowAddForm(!showAddForm)}
          className="flex items-center gap-2 bg-primary-600 hover:bg-primary-700 text-white px-4 py-2.5 rounded-lg text-sm font-semibold transition-colors"
        >
          <Plus className="w-4 h-4" />
          <span>Add Holding</span>
        </button>
      </div>

      {/* Add Holding Form Drawer/Card */}
      {showAddForm && (
        <div className="bg-card border border-border rounded-xl p-6 shadow-xl space-y-6">
          <h3 className="text-base font-bold text-white flex items-center gap-2">
            <Sparkles className="w-5 h-5 text-primary-500" />
            <span>Register New Asset Holding</span>
          </h3>

          <form onSubmit={handleSubmit} className="grid grid-cols-1 md:grid-cols-4 gap-6">
            {/* Asset Type */}
            <div>
              <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Asset Type</label>
              <select
                value={assetType}
                onChange={(e) => { setAssetType(e.target.value); resetForm(); }}
                className="w-full bg-[#0E1524] border border-border rounded-lg px-3 py-2.5 text-sm text-white focus:outline-none focus:border-primary-500"
              >
                <option value="Stock">Stock</option>
                <option value="ETF">ETF</option>
                <option value="MutualFund">Mutual Fund</option>
                <option value="FixedDeposit">Fixed Deposit</option>
                <option value="SavingsAccount">Savings Account</option>
              </select>
            </div>

            {/* Symbol or Name */}
            <div className="relative" onMouseLeave={() => setShowSuggestions(false)}>
              <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">
                {assetType === 'FixedDeposit' || assetType === 'SavingsAccount' ? 'Bank Name' : 'Symbol / Name'}
              </label>
              <input
                type="text"
                value={symbolOrName}
                onChange={(e) => handleSymbolChange(e.target.value)}
                onFocus={() => {
                  if (searchResults.length > 0) setShowSuggestions(true);
                }}
                placeholder={assetType === 'Stock' ? 'e.g. RELIANCE.NS' : assetType === 'SavingsAccount' ? 'e.g. HDFC Bank' : 'e.g. NIFTYBEES.NS'}
                className="w-full bg-[#0E1524] border border-border rounded-lg px-3.5 py-2 text-sm text-white focus:outline-none focus:border-primary-500"
                required
                autoComplete="off"
              />
              {isSearching && (
                <div className="absolute right-3 top-9 text-xs text-gray-400">Searching...</div>
              )}
              {showSuggestions && searchResults.length > 0 && (
                <div className="absolute z-50 left-0 right-0 mt-1 bg-[#151D30] border border-border rounded-lg shadow-xl max-h-60 overflow-y-auto">
                  {searchResults.map((item: any) => (
                    <button
                      key={item.symbol}
                      type="button"
                      onClick={() => handleSelectSuggestion(item)}
                      className="w-full text-left px-4 py-2 text-sm text-white hover:bg-primary-600/20 transition-colors border-b border-border last:border-0"
                    >
                      <div className="font-bold">{item.symbol}</div>
                      <div className="text-xs text-gray-400 truncate">{item.name} ({item.exchange})</div>
                    </button>
                  ))}
                </div>
              )}
            </div>

            {/* Conditional Form Inputs */}
            {(assetType === 'Stock' || assetType === 'ETF' || assetType === 'MutualFund') && (
              <>
                <div>
                  <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Quantity / Units</label>
                  <input
                    type="number"
                    step={assetType === 'MutualFund' ? "0.0001" : "1"}
                    value={quantity}
                    onChange={(e) => setQuantity(e.target.value)}
                    placeholder="0"
                    className="w-full bg-[#0E1524] border border-border rounded-lg px-3.5 py-2 text-sm text-white focus:outline-none focus:border-primary-500"
                    required
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Buy Price / NAV</label>
                  <input
                    type="number"
                    step="0.01"
                    value={buyPrice}
                    onChange={(e) => setBuyPrice(e.target.value)}
                    placeholder="0.00"
                    className="w-full bg-[#0E1524] border border-border rounded-lg px-3.5 py-2 text-sm text-white focus:outline-none focus:border-primary-500"
                    required
                  />
                </div>
              </>
            )}

            {assetType === 'FixedDeposit' && (
              <>
                <div>
                  <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Principal Amount</label>
                  <input
                    type="number"
                    step="0.01"
                    value={principal}
                    onChange={(e) => setPrincipal(e.target.value)}
                    placeholder="e.g. 50000"
                    className="w-full bg-[#0E1524] border border-border rounded-lg px-3.5 py-2 text-sm text-white focus:outline-none focus:border-primary-500"
                    required
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Interest Rate (%)</label>
                  <input
                    type="number"
                    step="0.01"
                    value={interestRate}
                    onChange={(e) => setInterestRate(e.target.value)}
                    placeholder="e.g. 5.5"
                    className="w-full bg-[#0E1524] border border-border rounded-lg px-3.5 py-2 text-sm text-white focus:outline-none focus:border-primary-500"
                    required
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Start Date</label>
                  <input
                    type="date"
                    value={startDate}
                    onChange={(e) => setStartDate(e.target.value)}
                    className="w-full bg-[#0E1524] border border-border rounded-lg px-3.5 py-2 text-sm text-white focus:outline-none focus:border-primary-500"
                    required
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Maturity Date</label>
                  <input
                    type="date"
                    value={maturityDate}
                    onChange={(e) => setMaturityDate(e.target.value)}
                    className="w-full bg-[#0E1524] border border-border rounded-lg px-3.5 py-2 text-sm text-white focus:outline-none focus:border-primary-500"
                    required
                  />
                </div>
              </>
            )}

            {assetType === 'SavingsAccount' && (
              <div>
                <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Initial Balance</label>
                <input
                  type="number"
                  step="0.01"
                  value={balance}
                  onChange={(e) => setBalance(e.target.value)}
                  placeholder="e.g. 10000"
                  className="w-full bg-[#0E1524] border border-border rounded-lg px-3.5 py-2 text-sm text-white focus:outline-none focus:border-primary-500"
                  required
                />
              </div>
            )}

            {/* Action Buttons */}
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
                disabled={createHoldingMutation.isPending}
                className="bg-primary-600 hover:bg-primary-700 text-white px-5 py-2 rounded-lg text-sm font-semibold transition-colors disabled:opacity-50"
              >
                {createHoldingMutation.isPending ? 'Saving...' : 'Register Holding'}
              </button>
            </div>
          </form>
        </div>
      )}

      {/* Holdings List Table */}
      <div className="bg-card border border-border rounded-xl overflow-hidden shadow-lg">
        {isLoading ? (
          <div className="p-12 text-center text-gray-400">Loading holdings records...</div>
        ) : holdings.length > 0 ? (
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="border-b border-border bg-[#0E1524] text-xs font-bold text-gray-400 uppercase tracking-wider">
                  <th className="px-6 py-4">Asset Type</th>
                  <th className="px-6 py-4">Symbol / Bank</th>
                  <th className="px-6 py-4 text-right">Holding Balance / Qty</th>
                  <th className="px-6 py-4 text-right">Cost Price / Rates</th>
                  <th className="px-6 py-4 text-right">Market Price</th>
                  <th className="px-6 py-4 text-right">Current Value</th>
                  <th className="px-6 py-4 text-right">Gain / Loss</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border text-sm">
                {holdings.map((h: any) => {
                  const isEquity = h.assetType === 'Stock' || h.assetType === 'ETF' || h.assetType === 'MutualFund';
                  return (
                    <tr key={h.id} className="hover:bg-[#121A2E]/40 transition-colors">
                      <td className="px-6 py-4">
                        <span className={`inline-flex items-center px-2.5 py-1 rounded-full text-xs font-semibold ${
                          h.assetType === 'Stock' ? 'bg-primary-500/10 text-primary-400' :
                          h.assetType === 'ETF' ? 'bg-indigo-500/10 text-indigo-400' :
                          h.assetType === 'MutualFund' ? 'bg-purple-500/10 text-purple-400' :
                          h.assetType === 'FixedDeposit' ? 'bg-warning-500/10 text-warning-500' :
                          'bg-accent-500/10 text-accent-500'
                        }`}>
                          {h.assetType}
                        </span>
                      </td>
                      <td className="px-6 py-4 font-bold text-white">{h.symbolOrName}</td>
                      <td className="px-6 py-4 text-right font-mono text-gray-300">
                        {isEquity ? h.quantityOrUnits?.toLocaleString(undefined, { minimumFractionDigits: 2 }) 
                                  : (h.assetType === 'FixedDeposit' ? 'FD Principal' : 'Savings')}
                      </td>
                      <td className="px-6 py-4 text-right font-mono text-gray-400">
                        {isEquity ? `₹${h.buyPriceOrNAV?.toFixed(2)}` 
                                  : (h.assetType === 'FixedDeposit' ? `${h.interestRate}% APY` : 'Cash')}
                      </td>
                      <td className="px-6 py-4 text-right font-mono text-gray-400">
                        {isEquity ? `₹${h.latestPrice?.toFixed(2)}` : '-'}
                      </td>
                      <td className="px-6 py-4 text-right font-mono font-bold text-white">
                        ₹{h.currentValue?.toLocaleString(undefined, { minimumFractionDigits: 2 })}
                      </td>
                      <td className={`px-6 py-4 text-right font-mono font-bold ${
                        h.totalGainLoss > 0 ? 'text-accent-500' : h.totalGainLoss < 0 ? 'text-danger-500' : 'text-gray-400'
                      }`}>
                        {h.totalGainLoss > 0 ? '+' : ''}
                        {h.totalGainLoss !== 0 ? `₹${h.totalGainLoss?.toLocaleString(undefined, { minimumFractionDigits: 2 })}` : '₹0.00'}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="p-12 text-center text-gray-500 flex flex-col items-center gap-4">
            <Briefcase className="w-12 h-12 text-gray-600" />
            <div>
              <p className="font-bold text-white">No assets registered yet</p>
              <p className="text-sm mt-1">Register a stock, mutual fund, or bank account to compile portfolio data.</p>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default Holdings;
