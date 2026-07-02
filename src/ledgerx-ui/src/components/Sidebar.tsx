import React from 'react';
import { NavLink, useNavigate } from 'react-router-dom';
import { 
  LayoutDashboard, 
  Briefcase, 
  History, 
  Bell, 
  Shield, 
  LogOut, 
  Activity 
} from 'lucide-react';
import api from '../services/api';

const Sidebar: React.FC = () => {
  const navigate = useNavigate();
  const role = localStorage.getItem('role');
  const username = localStorage.getItem('username') || 'User';

  const handleLogout = async () => {
    const refreshToken = localStorage.getItem('refreshToken');
    try {
      if (refreshToken) {
        await api.post('/auth/logout', { refreshToken });
      }
    } catch (e) {
      console.error('Logout request failed', e);
    } finally {
      localStorage.clear();
      navigate('/login');
    }
  };

  const navItems = [
    { to: '/', icon: <LayoutDashboard className="w-5 h-5" />, label: 'Dashboard' },
    { to: '/holdings', icon: <Briefcase className="w-5 h-5" />, label: 'Holdings' },
    { to: '/transactions', icon: <History className="w-5 h-5" />, label: 'Transactions' },
    { to: '/alerts', icon: <Bell className="w-5 h-5" />, label: 'Alerts' },
  ];

  return (
    <aside className="w-64 bg-card border-r border-border min-h-screen flex flex-col justify-between">
      <div>
        {/* Brand Header */}
        <div className="p-6 border-b border-border flex items-center gap-3">
          <div className="bg-primary-600 p-2 rounded-lg text-white">
            <Activity className="w-6 h-6" />
          </div>
          <div>
            <h1 className="font-bold text-lg text-white tracking-wider">LEDGERX</h1>
            <span className="text-xs text-gray-400 font-mono">DISTRIBUTED FI</span>
          </div>
        </div>

        {/* Navigation Items */}
        <nav className="p-4 space-y-1">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) =>
                `flex items-center gap-3 px-4 py-3 rounded-lg text-sm font-medium transition-all duration-200 ${
                  isActive
                    ? 'bg-primary-600 text-white shadow-lg shadow-primary-600/20'
                    : 'text-gray-400 hover:bg-border hover:text-white'
                }`
              }
            >
              {item.icon}
              <span>{item.label}</span>
            </NavLink>
          ))}

          {/* Admin Panel Only */}
          {role === 'Admin' && (
            <NavLink
              to="/admin"
              className={({ isActive }) =>
                `flex items-center gap-3 px-4 py-3 rounded-lg text-sm font-medium transition-all duration-200 ${
                  isActive
                    ? 'bg-red-600 text-white shadow-lg shadow-red-600/20'
                    : 'text-gray-400 hover:bg-border hover:text-white'
                }`
              }
            >
              <Shield className="w-5 h-5" />
              <span>Admin Panel</span>
            </NavLink>
          )}
        </nav>
      </div>

      {/* User Footer Profile */}
      <div className="p-4 border-t border-border space-y-3">
        <div className="flex items-center gap-3 px-2">
          <div className="w-8 h-8 rounded-full bg-border flex items-center justify-center font-bold text-primary-500 uppercase">
            {username.substring(0, 2)}
          </div>
          <div className="truncate">
            <h4 className="text-sm font-medium text-white truncate">{username}</h4>
            <span className="text-xs text-gray-500 capitalize font-mono">{role}</span>
          </div>
        </div>

        <button
          onClick={handleLogout}
          className="w-full flex items-center gap-3 px-4 py-2.5 rounded-lg text-sm font-medium text-red-400 hover:bg-red-500/10 hover:text-red-300 transition-colors duration-150"
        >
          <LogOut className="w-5 h-5" />
          <span>Sign Out</span>
        </button>
      </div>
    </aside>
  );
};

export default Sidebar;
