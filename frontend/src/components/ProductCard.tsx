import { useState } from "react";
import { Link } from "react-router-dom";
import type { Product } from "../api/types";
import { vnd } from "./format";

export function ProductArtwork({product, poster = false}: {product: Product; poster?: boolean}) {
  const [failed, setFailed] = useState<string[]>([]);
  const src = (poster ? [product.steam?.posterImage, product.steam?.headerImage] : [product.steam?.headerImage]).find((url): url is string => !!url && !failed.includes(url));
  const hue = [...product.name].reduce((n,c)=>n+c.charCodeAt(0),0)%360;
  return <div className="product-art" style={{background:`radial-gradient(ellipse at 75% 15%, hsl(${hue} 48% 38%), transparent 65%), linear-gradient(145deg, hsl(${hue} 30% 23%), #101014 85%)`}} aria-hidden="true">{src && <img className="steam-art" src={src} alt="" loading="lazy" onError={()=>setFailed(old=>[...old,src])}/>}<span className="art-orbit"/><span className="art-initial">{product.name.slice(0,2).toUpperCase()}</span><small>{product.category}</small></div>;
}
export function ProductCard({
  product,
  featured = false,
  availableKeys,
}: {
  product: Product;
  featured?: boolean;
  availableKeys?: number;
}) {
  const hasStockInfo = typeof availableKeys === "number";
  const inStock = hasStockInfo && availableKeys > 0;
  return (
    <Link className={`store-product ${featured ? "featured-product" : "compact-product"}`} to={`/products/${product.id}`}>
      <ProductArtwork product={product} poster />
      <div className="product-copy">
        <h3>{product.name}</h3>
        {hasStockInfo && (
          <div className={`product-stock ${inStock ? "in-stock" : "out-of-stock"}`}>
            <span className="stock-dot" aria-hidden="true" />
            {inStock ? `Còn ${availableKeys} key` : "Tạm hết key"}
          </div>
        )}
        <p>{product.steam ? "Steam" : product.category} · Digital key</p>
        <strong className="price">{vnd(product.price)}</strong>
        {!featured && <span className="product-arrow" aria-hidden="true">↗</span>}
      </div>
    </Link>
  );
}
