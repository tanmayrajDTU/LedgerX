import React from 'react';
import Sidebar from './Sidebar';

interface LayoutProps {
  children: React.ReactNode;
}

const Layout: React.FC<LayoutProps> = ({ children }) => {
  return (
    <div className="flex bg-background min-h-screen text-gray-100 overflow-hidden">
      {/* Navigation Sidebar */}
      <Sidebar />

      {/* Main Panel Content */}
      <main className="flex-1 h-screen overflow-y-auto p-8 lg:p-10">
        <div className="max-w-7xl mx-auto space-y-8">
          {children}
        </div>
      </main>
    </div>
  );
};

export default Layout;
