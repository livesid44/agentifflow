import { type ReactNode } from "react";

interface ConfigSectionProps {
  title: string;
  icon: ReactNode;
  description: string;
  children: ReactNode;
}

export default function ConfigSection({ title, icon, description, children }: ConfigSectionProps) {
  return (
    <section className="config-section">
      <div className="config-section-header">
        <div className="config-section-icon">{icon}</div>
        <div>
          <h2 className="config-section-title">{title}</h2>
          <p className="config-section-desc">{description}</p>
        </div>
      </div>
      <div className="config-section-body">{children}</div>
    </section>
  );
}
