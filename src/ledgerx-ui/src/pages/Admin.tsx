import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { 
  ShieldAlert, 
  Cpu, 
  Activity, 
  Settings, 
  Clock, 
  RefreshCw, 
  Play, 
  AlertOctagon, 
  CheckCircle,
  ToggleLeft,
  ToggleRight
} from 'lucide-react';
import api from '../services/api';

const Admin: React.FC = () => {
  const queryClient = useQueryClient();
  const [useLiveData, setUseLiveData] = useState(false);
  const [injectFault, setInjectFault] = useState(false);

  // Fetch admin job statistics and executions
  const { data: stats, isLoading: statsLoading } = useQuery({
    queryKey: ['adminStats'],
    queryFn: async () => {
      const response = await api.get('/admin/jobs');
      return response.data;
    },
    refetchInterval: 5000, // Refresh stats every 5 seconds for live dashboard logging!
  });

  // Fetch current market data config
  const { data: config } = useQuery({
    queryKey: ['adminConfig'],
    queryFn: async () => {
      const response = await api.get('/admin/config');
      setUseLiveData(response.data.useLiveData);
      setInjectFault(response.data.injectFault);
      return response.data;
    }
  });

  // Fetch worker service status
  const { data: workers = [] } = useQuery({
    queryKey: ['adminWorkers'],
    queryFn: async () => {
      const response = await api.get('/admin/workers');
      return response.data;
    }
  });

  // Update configuration mutation (saved in Redis)
  const updateConfigMutation = useMutation({
    mutationFn: async (newConfig: { useLiveData: boolean; injectFault: boolean }) => {
      await api.post('/admin/config', newConfig);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['adminConfig'] });
      alert('Distributed configuration updated in Redis! Background workers updated.');
    }
  });

  // Trigger manual scheduler execution
  const triggerSchedulerMutation = useMutation({
    mutationFn: async () => {
      await api.post('/admin/scheduler/trigger');
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['adminStats'] });
      alert('Scheduler triggered! Nightly pipeline jobs generated and published to RabbitMQ.');
    }
  });

  const handleSaveConfig = () => {
    updateConfigMutation.mutate({ useLiveData, injectFault });
  };

  return (
    <div className="space-y-8">
      {/* Header */}
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-3xl font-extrabold text-white tracking-tight">Admin System Console</h1>
          <p className="text-gray-400 text-sm mt-1">Distributed queues monitoring, worker health, and fault simulators.</p>
        </div>

        <button
          onClick={() => triggerSchedulerMutation.mutate()}
          disabled={triggerSchedulerMutation.isPending}
          className="flex items-center gap-2 bg-red-600 hover:bg-red-700 text-white px-5 py-3 rounded-lg text-sm font-semibold transition-colors disabled:opacity-50"
        >
          <Play className="w-4 h-4 fill-current" />
          <span>Manual Scheduler Run</span>
        </button>
      </div>

      {/* Pluggable Controls & Worker Liveness */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
        
        {/* Pluggable Configuration Card */}
        <div className="bg-card border border-border rounded-xl p-6 flex flex-col justify-between h-[280px]">
          <div>
            <h3 className="text-base font-bold text-white flex items-center gap-2 mb-2">
              <Settings className="w-5 h-5 text-primary-500" />
              <span>Pluggable Data Providers</span>
            </h3>
            <p className="text-xs text-gray-400 mb-6">
              Switch market data feeds dynamically. Fault injection tests consumer retries and DLQs.
            </p>

            <div className="space-y-4">
              {/* Live vs Mock */}
              <div className="flex items-center justify-between">
                <span className="text-sm font-semibold text-gray-300">Live Yahoo Finance Data</span>
                <button 
                  onClick={() => setUseLiveData(!useLiveData)}
                  className="text-primary-500 hover:text-primary-400 transition-colors"
                >
                  {useLiveData ? <ToggleRight className="w-10 h-10" /> : <ToggleLeft className="w-10 h-10 text-gray-600" />}
                </button>
              </div>

              {/* Inject Fault */}
              <div className="flex items-center justify-between">
                <div>
                  <span className="text-sm font-semibold text-gray-300 block">Inject Network Failure</span>
                  <span className="text-[10px] text-red-400 font-mono">Forces worker to throw exceptions</span>
                </div>
                <button 
                  onClick={() => setInjectFault(!injectFault)}
                  className="text-red-500 hover:text-red-400 transition-colors"
                >
                  {injectFault ? <ToggleRight className="w-10 h-10" /> : <ToggleLeft className="w-10 h-10 text-gray-600" />}
                </button>
              </div>
            </div>
          </div>

          <button
            onClick={handleSaveConfig}
            disabled={updateConfigMutation.isPending}
            className="w-full bg-[#121A2E] hover:bg-[#1E2942] border border-border text-white text-sm font-bold py-2.5 rounded-lg transition-colors mt-4"
          >
            {updateConfigMutation.isPending ? 'Updating...' : 'Save Configuration'}
          </button>
        </div>

        {/* Worker Service Health Cards */}
        <div className="lg:col-span-2 bg-card border border-border rounded-xl p-6 h-[280px] flex flex-col">
          <h3 className="text-base font-bold text-white flex items-center gap-2 mb-4">
            <Cpu className="w-5 h-5 text-accent-500" />
            <span>Distributed Workers Status</span>
          </h3>
          
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4 flex-grow">
            {workers.map((w: any) => (
              <div key={w.name} className="bg-[#0E1524] border border-border p-4 rounded-lg flex flex-col justify-between">
                <div>
                  <span className="font-bold text-white text-sm block">{w.name}</span>
                  <span className="text-[10px] font-mono text-gray-500 block mt-1">{w.queueBound}</span>
                </div>
                <div className="flex items-center justify-between mt-4">
                  <span className="inline-flex items-center gap-1 text-xs text-accent-500 font-semibold">
                    <span className="w-2 h-2 bg-accent-500 rounded-full animate-pulse" />
                    {w.status}
                  </span>
                  <span className="text-xs text-gray-400 font-mono">{w.throughput}</span>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Operational Metrics Cards */}
      {statsLoading ? (
        <div className="text-center text-gray-400">Loading system parameters...</div>
      ) : (
        <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-6">
          <div className="bg-card border border-border rounded-xl p-5 text-center">
            <span className="text-[10px] font-bold text-gray-400 uppercase tracking-wider block mb-2">Total Jobs</span>
            <span className="text-2xl font-bold text-white font-mono">{stats.totalJobs}</span>
          </div>

          <div className="bg-card border border-border rounded-xl p-5 text-center">
            <span className="text-[10px] font-bold text-gray-400 uppercase tracking-wider block mb-2">Pending</span>
            <span className="text-2xl font-bold text-gray-500 font-mono">{stats.pendingJobs}</span>
          </div>

          <div className="bg-card border border-border rounded-xl p-5 text-center">
            <span className="text-[10px] font-bold text-gray-400 uppercase tracking-wider block mb-2">Processing</span>
            <span className="text-2xl font-bold text-primary-500 font-mono">{stats.processingJobs}</span>
          </div>

          <div className="bg-card border border-border rounded-xl p-5 text-center">
            <span className="text-[10px] font-bold text-gray-400 uppercase tracking-wider block mb-2">Completed</span>
            <span className="text-2xl font-bold text-accent-500 font-mono">{stats.completedJobs}</span>
          </div>

          <div className="bg-card border border-border rounded-xl p-5 text-center">
            <span className="text-[10px] font-bold text-gray-400 uppercase tracking-wider block mb-2">DLQ / Rejections</span>
            <span className={`text-2xl font-bold font-mono ${stats.deadLetterJobs > 0 ? 'text-red-500' : 'text-gray-500'}`}>
              {stats.deadLetterJobs}
            </span>
          </div>

          <div className="bg-card border border-border rounded-xl p-5 text-center">
            <span className="text-[10px] font-bold text-gray-400 uppercase tracking-wider block mb-2">Avg Duration</span>
            <span className="text-xl font-bold text-white font-mono">{stats.averageJobDurationMs}ms</span>
          </div>
        </div>
      )}

      {/* Recent Job Executions Audit Log */}
      <div className="bg-card border border-border rounded-xl p-6">
        <div className="flex justify-between items-center mb-4">
          <h3 className="text-base font-bold text-white flex items-center gap-2">
            <Clock className="w-5 h-5 text-gray-400" />
            <span>Job Execution Log (Live Retries)</span>
          </h3>
          <span className="text-xs text-gray-500 font-mono">Updates every 5s</span>
        </div>

        {statsLoading ? (
          <div className="p-8 text-center text-gray-400">Loading execution registry...</div>
        ) : stats.recentExecutions?.length > 0 ? (
          <div className="overflow-x-auto max-h-[400px]">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="border-b border-border bg-[#0E1524] text-[10px] font-bold text-gray-400 uppercase tracking-wider">
                  <th className="px-4 py-3">Started At</th>
                  <th className="px-4 py-3">Job ID</th>
                  <th className="px-4 py-3">Job Type</th>
                  <th className="px-4 py-3">Worker Name</th>
                  <th className="px-4 py-3 text-right">Retries</th>
                  <th className="px-4 py-3 text-right">Duration (ms)</th>
                  <th className="px-4 py-3">Execution Status</th>
                  <th className="px-4 py-3">Error details</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border text-xs font-mono">
                {stats.recentExecutions.map((e: any) => (
                  <tr key={e.id} className="hover:bg-[#121A2E]/40 transition-colors">
                    <td className="px-4 py-3 text-gray-500">
                      {new Date(e.startedAt).toLocaleTimeString()}
                    </td>
                    <td className="px-4 py-3 text-gray-400 text-[10px] truncate max-w-[80px]" title={e.jobId}>
                      {e.jobId.substring(0, 8)}...
                    </td>
                    <td className="px-4 py-3 font-semibold text-white">{e.jobType}</td>
                    <td className="px-4 py-3 text-gray-400">{e.workerName}</td>
                    <td className="px-4 py-3 text-right font-bold text-warning-500">{e.retryCount}</td>
                    <td className="px-4 py-3 text-right text-gray-300">
                      {e.durationMs ? `${e.durationMs}ms` : '-'}
                    </td>
                    <td className="px-4 py-3">
                      <span className={`inline-flex px-2 py-0.5 rounded text-[10px] font-bold uppercase ${
                        e.jobStatus === 'Completed' ? 'bg-accent-500/10 text-accent-500' :
                        e.jobStatus === 'Processing' ? 'bg-primary-500/10 text-primary-500' :
                        e.jobStatus === 'DeadLetter' ? 'bg-red-500/10 text-red-500 animate-pulse' :
                        'bg-warning-500/10 text-warning-500'
                      }`}>
                        {e.jobStatus}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-red-400 max-w-[200px] truncate" title={e.errorMessage || ''}>
                      {e.errorMessage || '-'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="p-8 text-center text-gray-500 text-sm">
            No background job executions recorded yet. Trigger the scheduler.
          </div>
        )}
      </div>
    </div>
  );
};

export default Admin;
