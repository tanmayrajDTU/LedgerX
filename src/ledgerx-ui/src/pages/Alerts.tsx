import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Bell, Plus, ShieldCheck, Check, Sparkles } from 'lucide-react';
import api from '../services/api';

const Alerts: React.FC = () => {
  const queryClient = useQueryClient();
  const [showAddForm, setShowAddForm] = useState(false);

  // Form states
  const [holdingId, setHoldingId] = useState('');
  const [alertType, setAlertType] = useState('Price');
  const [thresholdValue, setThresholdValue] = useState('');
  const [message, setMessage] = useState('');

  // Fetch alerts
  const { data: alerts = [], isLoading } = useQuery({
    queryKey: ['alertsList'],
    queryFn: async () => {
      const response = await api.get('/alerts');
      return response.data;
    }
  });

  // Fetch holdings (to select in form)
  const { data: holdings = [] } = useQuery({
    queryKey: ['holdingsListForAlerts'],
    queryFn: async () => {
      const response = await api.get('/holdings');
      return response.data;
    }
  });

  // Create alert mutation
  const createAlertMutation = useMutation({
    mutationFn: async (newAlert: any) => {
      const response = await api.post('/alerts', newAlert);
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['alertsList'] });
      queryClient.invalidateQueries({ queryKey: ['dashboardSummary'] });
      setShowAddForm(false);
      resetForm();
    },
    onError: (err: any) => {
      alert(err.response?.data?.message || 'Failed to register alert rule.');
    }
  });

  // Dismiss alert mutation
  const dismissAlertMutation = useMutation({
    mutationFn: async (id: string) => {
      await api.put(`/alerts/${id}/dismiss`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['alertsList'] });
      queryClient.invalidateQueries({ queryKey: ['dashboardSummary'] });
    }
  });

  const resetForm = () => {
    setHoldingId('');
    setAlertType('Price');
    setThresholdValue('');
    setMessage('');
  };

  const handleAlertTypeChange = (type: string) => {
    setAlertType(type);
    if (type !== 'Price') {
      setHoldingId(''); // Portfolio-wide rules don't need a specific holding
    }
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();

    const payload: any = {
      alertType,
      thresholdValue: parseFloat(thresholdValue),
      message
    };

    if (alertType === 'Price' && holdingId) {
      payload.holdingId = holdingId;
    }

    createAlertMutation.mutate(payload);
  };

  return (
    <div className="space-y-8">
      {/* Header */}
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-3xl font-extrabold text-white tracking-tight">Security Alerts</h1>
          <p className="text-gray-400 text-sm mt-1">Portfolio triggers, price watches, and risk margins.</p>
        </div>
        
        <button
          onClick={() => setShowAddForm(!showAddForm)}
          className="flex items-center gap-2 bg-primary-600 hover:bg-primary-700 text-white px-4 py-2.5 rounded-lg text-sm font-semibold transition-colors"
        >
          <Plus className="w-4 h-4" />
          <span>New Alert Rule</span>
        </button>
      </div>

      {/* Add Alert Rule Form */}
      {showAddForm && (
        <div className="bg-card border border-border rounded-xl p-6 shadow-xl space-y-6">
          <h3 className="text-base font-bold text-white flex items-center gap-2">
            <Sparkles className="w-5 h-5 text-primary-500" />
            <span>Configure Safety Watch Threshold</span>
          </h3>

          <form onSubmit={handleSubmit} className="grid grid-cols-1 md:grid-cols-4 gap-6">
            {/* Alert Type */}
            <div>
              <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Alert Type</label>
              <select
                value={alertType}
                onChange={(e) => handleAlertTypeChange(e.target.value)}
                className="w-full bg-[#0E1524] border border-border rounded-lg px-3 py-2.5 text-sm text-white focus:outline-none focus:border-primary-500"
                required
              >
                <option value="Price">Price Target (Stock/ETF/MF)</option>
                <option value="AllocationDrift">Asset Allocation Drift (%)</option>
                <option value="FDMaturity">Fixed Deposit Impending Maturity (Days)</option>
                <option value="PortfolioConcentration">Single Asset Concentration (%)</option>
              </select>
            </div>

            {/* Target Asset (Price Target only) */}
            <div>
              <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Target Asset</label>
              <select
                value={holdingId}
                onChange={(e) => setHoldingId(e.target.value)}
                disabled={alertType !== 'Price'}
                className="w-full bg-[#0E1524] border border-border rounded-lg px-3 py-2.5 text-sm text-white focus:outline-none focus:border-primary-500 disabled:opacity-30"
                required={alertType === 'Price'}
              >
                <option value="">-- Choose Asset --</option>
                {holdings
                  .filter((h: any) => h.assetType === 'Stock' || h.assetType === 'ETF' || h.assetType === 'MutualFund')
                  .map((h: any) => (
                    <option key={h.id} value={h.id}>
                      {h.symbolOrName} ({h.assetType})
                    </option>
                  ))}
              </select>
            </div>

            {/* Threshold Value */}
            <div>
              <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">
                Threshold Limit Value
              </label>
              <input
                type="number"
                step="0.01"
                value={thresholdValue}
                onChange={(e) => setThresholdValue(e.target.value)}
                placeholder={
                  alertType === 'Price' ? 'e.g. 2600.00' :
                  alertType === 'AllocationDrift' ? 'e.g. 80 (%)' :
                  alertType === 'FDMaturity' ? 'e.g. 15 (Days)' : 'e.g. 40 (%)'
                }
                className="w-full bg-[#0E1524] border border-border rounded-lg px-3.5 py-2 text-sm text-white focus:outline-none focus:border-primary-500"
                required
              />
            </div>

            {/* Custom Alert Message */}
            <div>
              <label className="block text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Alert Notification Message</label>
              <input
                type="text"
                value={message}
                onChange={(e) => setMessage(e.target.value)}
                placeholder="e.g. RELIANCE exceeds threshold limit!"
                className="w-full bg-[#0E1524] border border-border rounded-lg px-3.5 py-2 text-sm text-white focus:outline-none focus:border-primary-500"
                required
              />
            </div>

            {/* Actions */}
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
                disabled={createAlertMutation.isPending}
                className="bg-primary-600 hover:bg-primary-700 text-white px-5 py-2 rounded-lg text-sm font-semibold transition-colors disabled:opacity-50"
              >
                {createAlertMutation.isPending ? 'Registering...' : 'Register Rule'}
              </button>
            </div>
          </form>
        </div>
      )}

      {/* Alerts Rule Grid */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-8">
        {/* Active Alarms Card */}
        <div className="bg-card border border-border rounded-xl p-6 space-y-4">
          <h3 className="text-sm font-bold text-white uppercase tracking-wider mb-2 flex items-center gap-2">
            <span className="w-2.5 h-2.5 bg-red-500 rounded-full animate-ping" />
            <span>Currently Triggered Alarms</span>
          </h3>

          <div className="space-y-4">
            {isLoading ? (
              <div className="text-center text-gray-400 py-6">Loading logs...</div>
            ) : alerts.filter((a: any) => a.isTriggered).length > 0 ? (
              alerts
                .filter((a: any) => a.isTriggered)
                .map((a: any) => (
                  <div key={a.id} className="bg-red-500/5 border border-red-500/25 p-4 rounded-lg flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
                    <div>
                      <span className="text-xs font-mono text-red-400 block uppercase font-semibold">
                        {a.alertType} Triggered
                      </span>
                      <p className="text-sm text-white font-medium mt-1">{a.message}</p>
                      <span className="text-xs text-gray-500 block font-mono mt-1">
                        Triggered on {new Date(a.triggeredAt).toLocaleString()} | Current: {a.alertType === 'Price' ? '₹' : ''}{a.currentValue}{['AllocationDrift', 'PortfolioConcentration'].includes(a.alertType) ? '%' : (a.alertType === 'FDMaturity' ? ' Days' : '')}
                      </span>
                    </div>

                    <button
                      onClick={() => dismissAlertMutation.mutate(a.id)}
                      disabled={dismissAlertMutation.isPending}
                      className="flex items-center gap-1.5 bg-[#1F1722] hover:bg-[#2D1B28] border border-red-500/20 text-red-300 px-3.5 py-2 rounded-lg text-xs font-bold transition-colors shrink-0 disabled:opacity-50"
                    >
                      <Check className="w-3.5 h-3.5" />
                      <span>Clear</span>
                    </button>
                  </div>
                ))
            ) : (
              <div className="text-center text-gray-500 py-12 text-sm">
                No active alarms triggered. Portfolio satisfies safety margins.
              </div>
            )}
          </div>
        </div>

        {/* Configured Watch Rules List */}
        <div className="bg-card border border-border rounded-xl p-6 space-y-4">
          <h3 className="text-sm font-bold text-white uppercase tracking-wider mb-2 flex items-center gap-2">
            <Bell className="w-4 h-4 text-primary-500" />
            <span>Configured Watch Rules</span>
          </h3>

          <div className="space-y-3 overflow-y-auto max-h-[360px]">
            {isLoading ? (
              <div className="text-center text-gray-400 py-6">Loading watchlist...</div>
            ) : alerts.length > 0 ? (
              alerts.map((a: any) => (
                <div key={a.id} className="bg-[#0E1524] border border-border p-3.5 rounded-lg flex justify-between items-center text-sm">
                  <div>
                    <span className="font-bold text-white block">
                      {a.holdingName}
                    </span>
                    <span className="text-xs text-gray-400 mt-1 block">
                      Type: {a.alertType} | Target Limit: {a.alertType === 'Price' ? '₹' : ''}{a.thresholdValue}{['AllocationDrift', 'PortfolioConcentration'].includes(a.alertType) ? '%' : (a.alertType === 'FDMaturity' ? ' Days' : '')}
                    </span>
                  </div>
                  <div>
                    <span className={`inline-flex px-2.5 py-0.5 rounded-full text-xs font-bold ${
                      a.isTriggered ? 'bg-red-500/10 text-red-500' : 'bg-green-500/10 text-green-500'
                    }`}>
                      {a.isTriggered ? 'Alarming' : 'Monitoring'}
                    </span>
                  </div>
                </div>
              ))
            ) : (
              <div className="text-center text-gray-500 py-12 text-sm">
                No alert watch rules registered.
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default Alerts;
