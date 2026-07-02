import React from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { 
  TrendingUp, 
  TrendingDown, 
  IndianRupee, 
  Percent, 
  Activity, 
  RefreshCw, 
  AlertTriangle, 
  ShieldCheck, 
  Calendar 
} from 'lucide-react';
import { 
  AreaChart, 
  Area, 
  XAxis, 
  YAxis, 
  Tooltip, 
  ResponsiveContainer, 
  PieChart, 
  Pie, 
  Cell, 
  Legend,
  BarChart,
  Bar
} from 'recharts';
import api from '../services/api';

const COLORS = ['#3B82F6', '#10B981', '#F59E0B', '#EF4444', '#8B5CF6', '#EC4899'];

const Dashboard: React.FC = () => {
  const queryClient = useQueryClient();

  // Fetch Dashboard Summary
  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['dashboardSummary'],
    queryFn: async () => {
      const response = await api.get('/dashboard/summary');
      return response.data;
    },
    refetchInterval: 10000, // Poll every 10 seconds to show background worker progress!
  });

  // Force Recalculation Mutation
  const refreshMutation = useMutation({
    mutationFn: async () => {
      await api.post('/dashboard/refresh');
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['dashboardSummary'] });
      alert('Manual pipeline refresh dispatched. RabbitMQ queues are processing!');
    }
  });

  if (isLoading) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[60vh] gap-4">
        <RefreshCw className="w-10 h-10 text-primary-500 animate-spin" />
        <p className="text-gray-400 font-medium">Assembling LedgerX intelligence analytics...</p>
      </div>
    );
  }

  if (isError) {
    return (
      <div className="p-6 bg-red-500/10 border border-red-500/20 rounded-xl text-red-400 flex items-center gap-3">
        <AlertTriangle className="w-6 h-6" />
        <span>Failed to load dashboard metrics. Verify database connection.</span>
      </div>
    );
  }

  const isCalculating = data?.isCalculating;

  return (
    <div className="space-y-8">
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-3xl font-extrabold text-white tracking-tight">Portfolio Intelligence</h1>
          <p className="text-gray-400 text-sm mt-1">Real-time valuation, risks, and distributed queue logs.</p>
        </div>
        
        <button
          onClick={() => refreshMutation.mutate()}
          disabled={refreshMutation.isPending || isCalculating}
          className="flex items-center justify-center gap-2 bg-[#121A2E] hover:bg-[#1E2942] border border-border text-white px-5 py-3 rounded-lg text-sm font-semibold transition-colors disabled:opacity-50"
        >
          <RefreshCw className={`w-4 h-4 ${refreshMutation.isPending || isCalculating ? 'animate-spin' : ''}`} />
          <span>{isCalculating ? 'Workers Running...' : 'Force Pipeline Run'}</span>
        </button>
      </div>

      {/* Background Processing Indicator */}
      {isCalculating && (
        <div className="p-4 bg-primary-600/10 border border-primary-500/20 rounded-xl text-primary-400 text-sm flex items-center gap-3 animate-pulse">
          <Activity className="w-5 h-5 shrink-0" />
          <span>Nightly processing workers are building your portfolio statistics. Dashboard will populate in a few seconds...</span>
        </div>
      )}

      {/* High-Level Overview Metrics Cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-6">
        {/* Net Worth */}
        <div className="bg-card border border-border rounded-xl p-6">
          <div className="flex justify-between items-center text-gray-400 mb-4">
            <span className="text-xs font-bold uppercase tracking-wider">Net Worth</span>
            <IndianRupee className="w-5 h-5 text-primary-500" />
          </div>
          <h2 className="text-2xl font-bold text-white">₹{data.netWorth?.toLocaleString()}</h2>
          <span className="text-xs text-gray-500 font-mono">Current Valuation</span>
        </div>

        {/* Invested Amount */}
        <div className="bg-card border border-border rounded-xl p-6">
          <div className="flex justify-between items-center text-gray-400 mb-4">
            <span className="text-xs font-bold uppercase tracking-wider">Invested</span>
            <IndianRupee className="w-5 h-5 text-gray-400" />
          </div>
          <h2 className="text-2xl font-bold text-white">₹{data.investedAmount?.toLocaleString()}</h2>
          <span className="text-xs text-gray-500 font-mono">Principal Invested</span>
        </div>

        {/* Gain / Loss */}
        <div className="bg-card border border-border rounded-xl p-6">
          <div className="flex justify-between items-center text-gray-400 mb-4">
            <span className="text-xs font-bold uppercase tracking-wider">Gain / Loss</span>
            {data.totalGainLoss >= 0 ? (
              <TrendingUp className="w-5 h-5 text-accent-500" />
            ) : (
              <TrendingDown className="w-5 h-5 text-danger-500" />
            )}
          </div>
          <h2 className={`text-2xl font-bold ${data.totalGainLoss >= 0 ? 'text-accent-500' : 'text-danger-500'}`}>
            ₹{data.totalGainLoss?.toLocaleString()}
          </h2>
          <span className="text-xs text-gray-500 font-mono">
            {data.absoluteReturn?.toFixed(2)}% Return
          </span>
        </div>

        {/* Volatility */}
        <div className="bg-card border border-border rounded-xl p-6">
          <div className="flex justify-between items-center text-gray-400 mb-4">
            <span className="text-xs font-bold uppercase tracking-wider">Volatility</span>
            <Percent className="w-5 h-5 text-warning-500" />
          </div>
          <h2 className="text-2xl font-bold text-white">{data.volatility?.toFixed(2)}%</h2>
          <span className="text-xs text-gray-500 font-mono">Annualized StdDev</span>
        </div>

        {/* Diversification Score */}
        <div className="bg-card border border-border rounded-xl p-6">
          <div className="flex justify-between items-center text-gray-400 mb-4">
            <span className="text-xs font-bold uppercase tracking-wider">Diversification</span>
            <ShieldCheck className="w-5 h-5 text-accent-500" />
          </div>
          <h2 className="text-2xl font-bold text-white">{data.diversificationScore?.toFixed(0)}/100</h2>
          <span className="text-xs text-gray-500 font-mono">
            Max concentration: {data.concentrationRisk?.toFixed(1)}%
          </span>
        </div>
      </div>

      {/* Main Charts Row */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
        {/* Growth Area Chart */}
        <div className="lg:col-span-2 bg-card border border-border rounded-xl p-6 flex flex-col justify-between h-[420px]">
          <div className="mb-4">
            <h3 className="text-base font-bold text-white">Portfolio growth</h3>
            <span className="text-xs text-gray-400">Historical performance over last 30 days.</span>
          </div>
          <div className="flex-1 w-full min-h-0">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={data.portfolioGrowth} margin={{ top: 10, right: 10, left: -10, bottom: 0 }}>
                <defs>
                  <linearGradient id="growthGrad" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#3B82F6" stopOpacity={0.3}/>
                    <stop offset="95%" stopColor="#3B82F6" stopOpacity={0}/>
                  </linearGradient>
                </defs>
                <XAxis dataKey="Date" stroke="#4B5563" fontSize={11} tickLine={false} />
                <YAxis stroke="#4B5563" fontSize={11} tickLine={false} />
                <Tooltip 
                  contentStyle={{ backgroundColor: '#151D30', borderColor: '#1F2A45', borderRadius: '8px' }}
                  labelStyle={{ color: '#9CA3AF' }}
                  itemStyle={{ color: '#FFFFFF' }}
                />
                <Area type="monotone" dataKey="Value" stroke="#3B82F6" strokeWidth={2.5} fillOpacity={1} fill="url(#growthGrad)" />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </div>

        {/* Asset Allocation Pie Chart */}
        <div className="bg-card border border-border rounded-xl p-6 flex flex-col justify-between h-[420px]">
          <div>
            <h3 className="text-base font-bold text-white">Asset Allocation</h3>
            <span className="text-xs text-gray-400">Distribution across asset types.</span>
          </div>
          <div className="flex-1 w-full min-h-0 relative flex items-center justify-center">
            {data.assetAllocation?.length > 0 ? (
              <ResponsiveContainer width="100%" height="100%">
                <PieChart>
                  <Pie
                    data={data.assetAllocation}
                    cx="50%"
                    cy="45%"
                    innerRadius={60}
                    outerRadius={90}
                    paddingAngle={3}
                    dataKey="Value"
                    nameKey="AssetType"
                  >
                    {data.assetAllocation.map((entry: any, index: number) => (
                      <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                    ))}
                  </Pie>
                  <Tooltip 
                    contentStyle={{ backgroundColor: '#151D30', borderColor: '#1F2A45', borderRadius: '8px' }}
                    itemStyle={{ color: '#FFFFFF' }}
                  />
                  <Legend verticalAlign="bottom" height={36} iconSize={10} iconType="circle" />
                </PieChart>
              </ResponsiveContainer>
            ) : (
              <span className="text-gray-500 text-sm">No allocation data. Add holdings.</span>
            )}
          </div>
        </div>
      </div>

      {/* Bottom Widgets Row */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
        {/* Sector Allocation */}
        <div className="bg-card border border-border rounded-xl p-6 flex flex-col h-[320px]">
          <h4 className="text-sm font-bold text-white uppercase tracking-wider mb-4">Sector Distribution</h4>
          <div className="flex-grow min-h-0 w-full">
            {data.sectorAllocation?.length > 0 ? (
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={data.sectorAllocation} layout="vertical" margin={{ top: 0, right: 10, left: 10, bottom: 0 }}>
                  <XAxis type="number" stroke="#4B5563" fontSize={10} hide />
                  <YAxis dataKey="Sector" type="category" stroke="#9CA3AF" fontSize={11} width={80} tickLine={false} />
                  <Tooltip 
                    contentStyle={{ backgroundColor: '#151D30', borderColor: '#1F2A45', borderRadius: '8px' }}
                    itemStyle={{ color: '#FFFFFF' }}
                  />
                  <Bar dataKey="Value" fill="#3B82F6" radius={[0, 4, 4, 0]} barSize={10} />
                </BarChart>
              </ResponsiveContainer>
            ) : (
              <span className="text-gray-500 text-sm">No sector data.</span>
            )}
          </div>
        </div>

        {/* Top Gainers & Losers */}
        <div className="bg-card border border-border rounded-xl p-6 flex flex-col h-[320px] justify-between">
          <div>
            <h4 className="text-sm font-bold text-white uppercase tracking-wider mb-3">Top Gainers & Losers</h4>
            <div className="space-y-3">
              {/* Gainers */}
              {data.topGainers?.slice(0, 2).map((g: any) => (
                <div key={g.SymbolOrName} className="flex justify-between items-center bg-[#0F1626] border border-border p-2.5 rounded-lg text-sm">
                  <div>
                    <span className="font-bold text-white block">{g.SymbolOrName}</span>
                    <span className="text-xs text-gray-500">{g.AssetType}</span>
                  </div>
                  <div className="text-right">
                    <span className="text-accent-500 font-semibold block">+{g.GainLossPercentage}%</span>
                    <span className="text-xs text-gray-400">₹{g.GainLossAmount?.toLocaleString()}</span>
                  </div>
                </div>
              ))}
              {/* Losers */}
              {data.topLosers?.slice(0, 2).map((l: any) => (
                <div key={l.SymbolOrName} className="flex justify-between items-center bg-[#0F1626] border border-border p-2.5 rounded-lg text-sm">
                  <div>
                    <span className="font-bold text-white block">{l.SymbolOrName}</span>
                    <span className="text-xs text-gray-500">{l.AssetType}</span>
                  </div>
                  <div className="text-right">
                    <span className="text-danger-500 font-semibold block">{l.GainLossPercentage}%</span>
                    <span className="text-xs text-gray-400">₹{l.GainLossAmount?.toLocaleString()}</span>
                  </div>
                </div>
              ))}
              {data.topGainers?.length === 0 && data.topLosers?.length === 0 && (
                <span className="text-gray-500 text-sm block py-6 text-center">Price performance is build after market data runs.</span>
              )}
            </div>
          </div>
        </div>

        {/* Upcoming FD Maturities */}
        <div className="bg-card border border-border rounded-xl p-6 flex flex-col h-[320px]">
          <h4 className="text-sm font-bold text-white uppercase tracking-wider mb-3">FD Maturity Alerts</h4>
          <div className="space-y-3 overflow-y-auto flex-grow pr-1">
            {data.upcomingMaturities?.map((fd: any) => (
              <div key={fd.Bank} className="bg-[#0F1626] border border-border p-3 rounded-lg flex justify-between items-center text-sm">
                <div>
                  <span className="font-semibold text-white block">{fd.Bank}</span>
                  <span className="text-xs text-gray-400 flex items-center gap-1.5 mt-1">
                    <Calendar className="w-3.5 h-3.5" />
                    {new Date(fd.MaturityDate).toLocaleDateString()}
                  </span>
                </div>
                <div className="text-right">
                  <span className="text-white font-bold block">₹{fd.Principal?.toLocaleString()}</span>
                  <span className="text-xs text-warning-500 font-semibold">{fd.DaysRemaining} days left</span>
                </div>
              </div>
            ))}
            {data.upcomingMaturities?.length === 0 && (
              <div className="flex flex-col items-center justify-center h-48 text-gray-500 text-sm">
                <span>No FD maturities within next 30 days.</span>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default Dashboard;
