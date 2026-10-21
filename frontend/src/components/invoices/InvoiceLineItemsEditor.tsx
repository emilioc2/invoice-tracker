import { PlusIcon, TrashIcon } from "lucide-react";
import {
  useFieldArray,
  useWatch,
  type Control,
  type FieldErrors,
  type UseFormRegister,
} from "react-hook-form";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { formatCurrency } from "@/lib/utils";
import type { InvoiceFormInput } from "@/schemas/invoiceSchema";

interface InvoiceLineItemsEditorProps {
  control: Control<InvoiceFormInput>;
  register: UseFormRegister<InvoiceFormInput>;
  errors: FieldErrors<InvoiceFormInput>;
}

/**
 * Dynamic add/remove line-item rows (`useFieldArray`) with a live, client-side-computed total
 * per row and a running subtotal/tax/total preview below — display-only math that mirrors the
 * server's authoritative calculation (`LineTotal = Quantity * UnitPrice`, `Subtotal` = sum of
 * line totals, `TaxAmount = Subtotal * TaxRate`, `Total = Subtotal + TaxAmount`) so users see
 * what they're about to submit, but the server always has the final say.
 */
export function InvoiceLineItemsEditor({ control, register, errors }: InvoiceLineItemsEditorProps) {
  const { fields, append, remove } = useFieldArray({ control, name: "lineItems" });
  const lineItems = useWatch({ control, name: "lineItems" });
  const taxRatePercent = useWatch({ control, name: "taxRatePercent" });

  const rowTotals = (lineItems ?? []).map((item) => {
    const quantity = Number(item?.quantity) || 0;
    const unitPrice = Number(item?.unitPrice) || 0;
    return quantity * unitPrice;
  });
  const subtotal = rowTotals.reduce((sum, total) => sum + total, 0);
  const taxRate = (Number(taxRatePercent) || 0) / 100;
  const taxAmount = subtotal * taxRate;
  const total = subtotal + taxAmount;

  const lineItemsError = errors.lineItems;
  const rootLineItemsMessage =
    lineItemsError && "message" in lineItemsError ? lineItemsError.message : undefined;

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <Label>Line items</Label>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => append({ description: "", quantity: 1, unitPrice: 0 })}
        >
          <PlusIcon className="size-3.5" />
          Add line item
        </Button>
      </div>

      {rootLineItemsMessage && (
        <p className="text-sm text-destructive">{String(rootLineItemsMessage)}</p>
      )}

      <div className="space-y-3 rounded-lg border border-border p-4">
        {fields.length === 0 ? (
          <p className="text-sm text-muted-foreground">No line items yet.</p>
        ) : (
          fields.map((field, index) => {
            const rowErrors = errors.lineItems?.[index];
            return (
              <div key={field.id} className="grid grid-cols-12 gap-2 items-start">
                <div className="col-span-5 space-y-1">
                  {index === 0 && <Label htmlFor={`line-item-${index}-description`}>Description</Label>}
                  <Input
                    id={`line-item-${index}-description`}
                    placeholder="Description"
                    aria-invalid={!!rowErrors?.description}
                    aria-describedby={
                      rowErrors?.description ? `line-item-${index}-description-error` : undefined
                    }
                    {...register(`lineItems.${index}.description`)}
                  />
                  {rowErrors?.description && (
                    <p id={`line-item-${index}-description-error`} className="text-xs text-destructive">
                      {rowErrors.description.message}
                    </p>
                  )}
                </div>

                <div className="col-span-2 space-y-1">
                  {index === 0 && <Label htmlFor={`line-item-${index}-quantity`}>Quantity</Label>}
                  <Input
                    id={`line-item-${index}-quantity`}
                    type="number"
                    step="any"
                    aria-invalid={!!rowErrors?.quantity}
                    aria-describedby={
                      rowErrors?.quantity ? `line-item-${index}-quantity-error` : undefined
                    }
                    {...register(`lineItems.${index}.quantity`)}
                  />
                  {rowErrors?.quantity && (
                    <p id={`line-item-${index}-quantity-error`} className="text-xs text-destructive">
                      {rowErrors.quantity.message}
                    </p>
                  )}
                </div>

                <div className="col-span-2 space-y-1">
                  {index === 0 && <Label htmlFor={`line-item-${index}-unitPrice`}>Unit price</Label>}
                  <Input
                    id={`line-item-${index}-unitPrice`}
                    type="number"
                    step="any"
                    aria-invalid={!!rowErrors?.unitPrice}
                    aria-describedby={
                      rowErrors?.unitPrice ? `line-item-${index}-unitPrice-error` : undefined
                    }
                    {...register(`lineItems.${index}.unitPrice`)}
                  />
                  {rowErrors?.unitPrice && (
                    <p id={`line-item-${index}-unitPrice-error`} className="text-xs text-destructive">
                      {rowErrors.unitPrice.message}
                    </p>
                  )}
                </div>

                <div className="col-span-2 space-y-1">
                  {index === 0 && <Label>Line total</Label>}
                  <p className="h-8 text-sm leading-8">{formatCurrency(rowTotals[index] ?? 0)}</p>
                </div>

                <div className="col-span-1 flex items-end justify-end">
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`Remove line item ${index + 1}`}
                    onClick={() => remove(index)}
                  >
                    <TrashIcon className="size-4" />
                  </Button>
                </div>
              </div>
            );
          })
        )}
      </div>

      <div className="flex flex-col items-end gap-1 text-sm">
        <div className="flex w-48 justify-between">
          <span className="text-muted-foreground">Subtotal</span>
          <span>{formatCurrency(subtotal)}</span>
        </div>
        <div className="flex w-48 justify-between">
          <span className="text-muted-foreground">Tax</span>
          <span>{formatCurrency(taxAmount)}</span>
        </div>
        <div className="flex w-48 justify-between font-medium">
          <span>Total</span>
          <span>{formatCurrency(total)}</span>
        </div>
      </div>
    </div>
  );
}
