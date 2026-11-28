import { useLanguage } from "../i18n/LanguageContext";

export function RefreshButton({
  loading = false,
  disabled = false,
  onClick,
  title,
  className = "",
}: {
  loading?: boolean;
  disabled?: boolean;
  onClick: () => void;
  title?: string;
  className?: string;
}) {
  const { t } = useLanguage();
  const label = title ?? t("Làm mới", "Refresh");

  return (
    <button
      type="button"
      className={`refresh-btn${className ? ` ${className}` : ""}`}
      title={label}
      aria-label={label}
      aria-busy={loading}
      disabled={disabled || loading}
      onClick={onClick}
    >
      <svg
        className={loading ? "refresh-icon spinning" : "refresh-icon"}
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
      >
        <path d="M20 7v5h-5" />
        <path d="M4 17v-5h5" />
        <path d="M6.1 6.1A8.3 8.3 0 0 1 20 12" />
        <path d="M4 12a8.3 8.3 0 0 0 13.9 5.9" />
      </svg>
    </button>
  );
}
