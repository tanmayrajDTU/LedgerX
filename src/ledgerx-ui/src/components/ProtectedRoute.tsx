import React from 'react';
import { Navigate, Outlet } from 'react-router-dom';

interface ProtectedRouteProps {
  requiredRole?: 'User' | 'Admin';
}

const ProtectedRoute: React.FC<ProtectedRouteProps> = ({ requiredRole }) => {
  const token = localStorage.getItem('accessToken');
  const role = localStorage.getItem('role');

  if (!token) {
    return <Navigate to="/login" replace />;
  }

  if (requiredRole && role !== requiredRole) {
    // If user is not admin but tries to access admin pages, redirect to dashboard
    return <Navigate to="/" replace />;
  }

  return <Outlet />;
};

export default ProtectedRoute;
