import { Route, Routes } from "react-router-dom";
import { Navbar } from "./components/Navbar";
import { HomePage } from "./pages/HomePage";
import { ProductPage } from "./pages/ProductPage";
import { OrdersPage } from "./pages/OrdersPage";
import { PaymentResultPage } from "./pages/PaymentResultPage";
import { AdminPage } from "./pages/AdminPage";
import { NotFoundPage } from "./pages/NotFoundPage";

export default function App() {
  return (
    <>
      <Navbar />
      <Routes>
        <Route path="/" element={<HomePage />} />
        <Route path="/products/:id" element={<ProductPage />} />
        <Route path="/orders" element={<OrdersPage />} />
        <Route path="/payment/result" element={<PaymentResultPage />} />
        <Route path="/admin" element={<AdminPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </>
  );
}
