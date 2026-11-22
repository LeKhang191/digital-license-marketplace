import { Link } from "react-router-dom";
import type { Product } from "../api/types";
import { vnd } from "./format";

export function ProductCard({ product }: { product: Product }) {
  return (
    <Link to={`/products/${product.id}`} style={{ color: "inherit", textDecoration: "none" }}>
      <div className="card">
        <div className="thumb">{product.name.slice(0, 2).toUpperCase()}</div>
        <div className="muted">{product.category}</div>
        <h2>{product.name}</h2>
        <div className="price">{vnd(product.price)}</div>
      </div>
    </Link>
  );
}
