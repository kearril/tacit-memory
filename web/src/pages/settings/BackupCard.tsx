import { Archive } from "lucide-react";
import { useDictionary } from "../../app/session-context";
import { Card, CardDescription, CardTitle } from "../../components/ui/card";
import { Setting, type SettingsForm } from "./fields";
import { sectionIdOf } from "./form";
import { SecretField } from "./SecretField";

/**
 * The backup card: how many automatic backups to keep, then the cloud backup's WebDAV folder, user name and password.
 * Emptying the folder and the user name turns the cloud backup off.
 */
export function BackupCard({ form, className }: { readonly form: SettingsForm; readonly className?: string }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.backup;

  return (
    <Card id={sectionIdOf("backup")} aria-labelledby="backup-title" className={`scroll-mt-xl ${className ?? ""}`}>
      <header className="flex items-start gap-md">
        <span className="grid size-(--tk-size-control) shrink-0 place-items-center rounded-md bg-accent-soft text-accent">
          <Archive aria-hidden className="size-(--tk-font-size-lg)" />
        </span>
        <div className="grid min-w-0 flex-1 gap-xs">
          <CardTitle id="backup-title">{text.title}</CardTitle>
          <CardDescription>{text.summary}</CardDescription>
        </div>
      </header>

      <section aria-labelledby="backup-local" className="grid gap-md">
        <h3 id="backup-local" className="text-sm font-semibold">
          {text.local}
        </h3>
        <Setting form={form} path="backup.keep" text={text.fields.keep} />
      </section>

      <section aria-labelledby="backup-cloud" className="grid gap-md border-t-(length:--tk-border-thin) border-line pt-md">
        <h3 id="backup-cloud" className="text-sm font-semibold">
          {text.cloud}
        </h3>
        <Setting form={form} path="backup.webdav.url" text={text.fields.url} defaultText={text.urlDefault} wide />
        <Setting form={form} path="backup.webdav.username" text={text.fields.username} defaultText={text.usernameDefault} />
        <SecretField form={form} path="backup.webdav.password" text={text.fields.password} />
      </section>
    </Card>
  );
}
