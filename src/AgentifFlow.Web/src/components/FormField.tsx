interface FormFieldProps {
  id: string;
  label: string;
  value: string;
  onChange: (val: string) => void;
  placeholder?: string;
  type?: "text" | "password" | "url";
  hint?: string;
  required?: boolean;
}

export default function FormField({
  id,
  label,
  value,
  onChange,
  placeholder,
  type = "text",
  hint,
  required,
}: FormFieldProps) {
  return (
    <div className="form-field">
      <label htmlFor={id} className="form-label">
        {label}
        {required && <span className="required-mark" aria-hidden="true"> *</span>}
      </label>
      <input
        id={id}
        type={type}
        className="form-input"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        autoComplete={type === "password" ? "current-password" : undefined}
      />
      {hint && <p className="form-hint">{hint}</p>}
    </div>
  );
}
