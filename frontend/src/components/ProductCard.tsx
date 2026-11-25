import { Link } from "react-router-dom";
import type { Product } from "../api/types";
import { vnd } from "./format";

export function ProductArtwork({product}: {product: Product}) {
  const hue = [...product.name].reduce((n,c)=>n+c.charCodeAt(0),0)%360;
  return <div className="product-art" style={{background:`radial-gradient(ellipse at 75% 15%, hsl(${hue} 48% 38%), transparent 65%), linear-gradient(145deg, hsl(${hue} 30% 23%), #101014 85%)`}} aria-hidden="true"><span className="art-orbit"/><span className="art-initial">{product.name.slice(0,2).toUpperCase()}</span><small>{product.category}</small></div>;
}
export function ProductCard({ product, featured = false }: { product: Product; featured?: boolean }) {
  return <Link className={`store-product ${featured ? "featured-product" : "compact-product"}`} to={`/products/${product.id}`}><ProductArtwork product={product}/><div className="product-copy"><h3>{product.name}</h3><p>{product.category} · Digital key</p><strong className="price">{vnd(product.price)}</strong>{!featured && <span className="product-arrow" aria-hidden="true">↗</span>}</div></Link>;
}
