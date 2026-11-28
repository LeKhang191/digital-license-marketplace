import { useEffect, useState, type FormEvent } from "react";
import { NavLink } from "react-router-dom";
import { listSuppliers, saveSupplier, listSupplierBatches, type Supplier, type SupplierInput, type ImportBatch } from "../api/licenses";
import type { Product } from "../api/types";

const blank: SupplierInput = {name:"",website:null,email:null,phone:null,notes:null,isActive:true};
const money = (n:number) => n.toLocaleString("vi-VN") + " đ";
const message = (e:unknown) => e instanceof Error ? e.message : "Không thể thực hiện yêu cầu.";

export function SupplierDirectory({products}: {products: Product[]}) {
  const [rows,setRows] = useState<Supplier[]>([]);
  const [loading,setLoading] = useState(true);
  const [error,setError] = useState("");
  const [version,refresh] = useState(0);
  const [search,setSearch] = useState("");
  const [filter,setFilter] = useState("all");
  const [editing,setEditing] = useState<string|null>(null);
  const [form,setForm] = useState<SupplierInput>(blank);
  const [busy,setBusy] = useState(false);
  const [formError,setFormError] = useState("");
  const [selected,setSelected] = useState("");
  const [batches,setBatches] = useState<ImportBatch[]>([]);
  const [detailLoading,setDetailLoading] = useState(false);
  const [detailError,setDetailError] = useState("");
  useEffect(() => {
    let live=true; setLoading(true); setError("");
    listSuppliers().then(v=>{if(live)setRows(v);}).catch(e=>{if(live)setError(message(e));}).finally(()=>{if(live)setLoading(false);});
    return ()=>{live=false;};
  },[version]);
  useEffect(()=>{
    setBatches([]); setDetailError(""); setDetailLoading(!!selected);
    if(!selected)return;
    let live=true;
    listSupplierBatches(selected).then(v=>{if(live)setBatches(v);}).catch(e=>{if(live)setDetailError(message(e));}).finally(()=>{if(live)setDetailLoading(false);});
    return ()=>{live=false;};
  },[selected,version]);
  const detail=rows.find(s=>s.id===selected);
  const filtered=rows.filter(s=>s.name.toLowerCase().includes(search.trim().toLowerCase()) && (filter==="all" || s.isActive===(filter==="active")));
  function edit(s?:Supplier) {
    setEditing(s?.id ?? "new"); setForm(s ? {name:s.name,website:s.website,email:s.email,phone:s.phone,notes:s.notes,isActive:s.isActive} : {...blank}); setFormError("");
  }
  async function submit(e:FormEvent) {
    e.preventDefault(); if(busy || !editing)return;
    setBusy(true); setFormError("");
    try {await saveSupplier({...form,name:form.name.trim(),website:form.website?.trim()||null,email:form.email?.trim()||null,phone:form.phone?.trim()||null,notes:form.notes?.trim()||null},editing==="new"?undefined:editing);setEditing(null);refresh(v=>v+1);}
    catch(e){setFormError(message(e));}finally{setBusy(false);}
  }
  return <>
    <div className="grid admin-summary license-summary">
      <div className="card"><h2>Nhà cung cấp</h2><strong>{loading||error?"—":rows.length}</strong></div>
      <div className="card"><h2>Đang hoạt động</h2><strong>{loading||error?"—":rows.filter(s=>s.isActive).length}</strong></div>
      <div className="card"><h2>Key đã nhập</h2><strong>{loading||error?"—":rows.reduce((n,s)=>n+s.imported,0)}</strong></div>
    </div>
    <section className="card admin-form license-form">
      <div className="supplier-heading"><h2>Nguồn hàng thủ công / CSV / TXT</h2><button type="button" className="btn" disabled={busy} onClick={()=>edit()}>+ Thêm nhà cung cấp</button></div>
      <p className="muted">Khai báo nguồn hàng không tự tạo kết nối API. Các tích hợp API có công cụ riêng bên dưới.</p>
      <div className="supplier-filters"><label>Tìm nhà cung cấp<input value={search} onChange={e=>setSearch(e.target.value)} placeholder="Tên nhà cung cấp" /></label><label>Trạng thái<select value={filter} onChange={e=>setFilter(e.target.value)}><option value="all">Tất cả</option><option value="active">Đang hoạt động</option><option value="inactive">Ngừng hoạt động</option></select></label></div>
      {error && <p role="alert">{error} <button type="button" onClick={()=>refresh(v=>v+1)}>Thử lại</button></p>}
      {loading ? <p role="status">Đang tải...</p> : !error && !filtered.length ? <p className="muted">Không có nhà cung cấp phù hợp.</p> : null}
      <div className="supplier-cards">{filtered.map(s=><article className="card supplier-card" key={s.id}>
        <div className="supplier-heading"><h3>{s.name}</h3><span className={s.isActive?"license-available":"muted"}>{s.isActive?"Đang hoạt động":"Ngừng hoạt động"}</span></div>
        <p className="muted">{s.batchCount} lô · {s.imported} key đã nhập</p><p>Giá vốn đã ghi nhận: {money(s.knownCostVnd)}</p>
        {s.unknownCostKeys>0 && <p className="muted">{s.unknownCostKeys} key chưa có giá vốn</p>}
        <div className="supplier-actions"><button type="button" className="btn btn-secondary" onClick={()=>setSelected(s.id)}>Chi tiết</button><button type="button" className="btn btn-secondary" disabled={busy} onClick={()=>edit(s)}>Chỉnh sửa</button>{s.isActive && <NavLink className="btn" to={`/licenses/import?supplier=${encodeURIComponent(s.id)}`}>Nhập kho</NavLink>}</div>
      </article>)}</div>
    </section>
    {editing!==null && <form className="card admin-form license-form" onSubmit={submit}>
      <div className="supplier-heading"><h2>{editing==="new"?"Thêm nhà cung cấp":"Chỉnh sửa nhà cung cấp"}</h2><button type="button" disabled={busy} onClick={()=>setEditing(null)}>Đóng</button></div>
      <fieldset disabled={busy}><div className="license-form">
        <label>Tên nhà cung cấp *<input required maxLength={160} value={form.name} onChange={e=>setForm(v=>({...v,name:e.target.value}))}/></label>
        <label>Website<input type="url" maxLength={500} value={form.website??""} onChange={e=>setForm(v=>({...v,website:e.target.value}))}/></label>
        <label>Email<input type="email" maxLength={255} value={form.email??""} onChange={e=>setForm(v=>({...v,email:e.target.value}))}/></label>
        <label>Điện thoại<input maxLength={80} value={form.phone??""} onChange={e=>setForm(v=>({...v,phone:e.target.value}))}/></label>
        <label>Ghi chú<textarea maxLength={2000} value={form.notes??""} onChange={e=>setForm(v=>({...v,notes:e.target.value}))}/></label>
        <label className="license-test-toggle"><input type="checkbox" checked={form.isActive} onChange={e=>setForm(v=>({...v,isActive:e.target.checked}))}/> Đang hoạt động</label>
        <p className="muted">Bỏ chọn để ngừng nhập hàng; lịch sử vẫn được giữ.</p>
        {formError && <p role="alert">{formError}</p>}
        <button type="submit" disabled={!form.name.trim() || busy}>{busy?"Đang lưu...":"Lưu nhà cung cấp"}</button>
      </div></fieldset>
    </form>}
    {detail && <section className="card admin-table-container">
      <div className="supplier-heading"><h2>{detail.name} — chi tiết</h2><button type="button" onClick={()=>setSelected("")}>Đóng</button></div>
      <p>Website: {detail.website||"—"} · Email: {detail.email||"—"} · Điện thoại: {detail.phone||"—"}</p><p>{detail.notes}</p>
      <p>{detail.batchCount} lô · {detail.imported} key · {detail.duplicates} trùng · {money(detail.knownCostVnd)} giá vốn đã ghi nhận</p>
      <p className="muted">200 lô gần nhất của nguồn này; tổng số liệu tính trên toàn bộ lịch sử.</p>
      {detailLoading && <p role="status">Đang tải lịch sử...</p>}{detailError && <p role="alert">{detailError}</p>}
      <div style={{overflowX:"auto"}}><table className="admin-table"><thead><tr><th>Ngày / mã lô</th><th>Sản phẩm</th><th>Nguồn lúc nhập</th><th>Nhập / trùng / lỗi</th><th>Giá vốn</th></tr></thead><tbody>{batches.map(b=><tr key={b.id}><td>{new Date(b.createdAtUtc).toLocaleString("vi-VN")}<br/>{b.reference||b.id.slice(0,8)}</td><td>{products.find(p=>p.id===b.productId)?.name??b.productId}</td><td>{b.supplierName}</td><td>{b.imported} / {b.duplicates} / {b.invalid}</td><td>{b.unitCostVnd==null?"Chưa khai báo":money(b.unitCostVnd*b.imported)}</td></tr>)}</tbody></table></div>
      {!detailLoading&&!detailError&&!batches.length&&<p className="muted">Chưa có lô nhập.</p>}
    </section>}
  </>;
}
