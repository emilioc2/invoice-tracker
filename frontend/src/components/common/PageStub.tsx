/** Minimal placeholder body for a route page — proves routing/layout without real content yet. */
export function PageStub({ title }: { title: string }) {
  return (
    <div className="space-y-2">
      <h1 className="text-2xl font-semibold">{title}</h1>
      <p className="text-sm text-muted-foreground">TODO: step 11</p>
    </div>
  );
}
