export function previewLicenseFile(text: string, csv: boolean) {
  const lines = text.replace(/^\uFEFF/, "").split(/\r\n|\n|\r/).filter(line => line.trim());
  const keys: string[] = [];
  for (const [index, line] of lines.entries()) {
    let key = line.trim();
    if (csv) {
      if (key.startsWith('"')) {
        if (!/^"(?:[^"]|"")*"$/.test(key)) throw new Error("CSV mẫu chỉ hỗ trợ một cột, mỗi key trên một dòng.");
        key = key.slice(1, -1).replace(/""/g, '"').trim();
      } else if (/[,"]/.test(key)) throw new Error("CSV phải có đúng một cột license_key.");
      if (index === 0 && key.toLowerCase() === "license_key") continue;
    }
    keys.push(key);
  }
  if (!keys.length || keys.length > 1000) throw new Error("File phải chứa từ 1 đến 1.000 key.");
  let invalid = 0, duplicates = 0;
  const seen = new Set<string>();
  for (const key of keys) {
    if (key.length < 5 || key.length > 512 || /[\u0000-\u001f\u007f-\u009f]/.test(key)) { invalid++; continue; }
    if (seen.has(key)) duplicates++;
    else seen.add(key);
  }
  return { total: keys.length, invalid, duplicates, candidates: seen.size };
}

export function downloadLicenseTemplate(csv: boolean) {
  const rows = ["DEMO-KEY-00001", "DEMO-KEY-00002", "DEMO-KEY-00003"];
  const content = (csv ? "license_key\n" : "") + rows.join("\n") + "\n";
  const url = URL.createObjectURL(new Blob([content], { type: csv ? "text/csv" : "text/plain" }));
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = csv ? "license-template.csv" : "license-template.txt";
  anchor.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}
