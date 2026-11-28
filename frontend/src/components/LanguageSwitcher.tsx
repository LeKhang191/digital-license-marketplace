import { useLanguage } from "../i18n/LanguageContext";

export function LanguageSwitcher() {
  const { lang, toggleLang, t } = useLanguage();

  return (
    <button
      type="button"
      className="lang-switcher no-print"
      onClick={toggleLang}
      title={t(
        "Chuyển sang tiếng Anh (English)",
        "Switch to Vietnamese (Tiếng Việt)",
      )}
      aria-label={t(
        "Chuyển đổi ngôn ngữ Anh - Việt",
        "Switch language English - Vietnamese",
      )}
    >
      <svg
        width="17"
        height="17"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.9"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
      >
        <circle cx="12" cy="12" r="9.5" />
        <path d="M2.5 12h19" />
        <path d="M12 2.5a14.5 14.5 0 0 1 0 19 14.5 14.5 0 0 1 0-19Z" />
      </svg>
      <span className="lang-code">{lang === "vi" ? "VI" : "EN"}</span>
      <span className="lang-sep" aria-hidden="true">/</span>
      <span className="lang-alt">{lang === "vi" ? "EN" : "VI"}</span>
    </button>
  );
}
