import { Navigate, Route, Routes } from "react-router-dom";
import { AuthProvider } from "./context/AuthContext";
import { PrivateRoute } from "./components/PrivateRoute";
import { Layout } from "./components/Layout";
import { LoginPage } from "./pages/LoginPage";
import { RegisterPage } from "./pages/RegisterPage";
import { SubscribePage } from "./pages/SubscribePage";
import { DashboardPage } from "./pages/DashboardPage";
import { StudentsPage } from "./pages/StudentsPage";
import { CheckinPointsPage } from "./pages/CheckinPointsPage";
import { CheckinsPage } from "./pages/CheckinsPage";
import { ReportsPage } from "./pages/ReportsPage";

function Protected({ children }: { children: React.ReactNode }) {
  return (
    <PrivateRoute>
      <Layout>{children}</Layout>
    </PrivateRoute>
  );
}

export default function App() {
  return (
    <AuthProvider>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/cadastro" element={<RegisterPage />} />
        <Route path="/assinatura" element={<PrivateRoute><SubscribePage /></PrivateRoute>} />
        <Route path="/" element={<Protected><DashboardPage /></Protected>} />
        <Route path="/alunos" element={<Protected><StudentsPage /></Protected>} />
        <Route path="/pontos-de-checkin" element={<Protected><CheckinPointsPage /></Protected>} />
        <Route path="/checkins" element={<Protected><CheckinsPage /></Protected>} />
        <Route path="/relatorios" element={<Protected><ReportsPage /></Protected>} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </AuthProvider>
  );
}
