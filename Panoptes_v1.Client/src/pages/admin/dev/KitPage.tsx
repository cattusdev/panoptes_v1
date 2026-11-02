import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { z } from 'zod'
import { Archive, CircleAlert, CircleCheck, Info, Loader2, TriangleAlert } from 'lucide-react'
import { SettingsCard } from '@/components/admin/Common'
import { ColumnsMenu, DataTable, type Column } from '@/components/admin/DataTable'
import { MoneyText } from '@/components/admin/MoneyText'
import { PageHeader } from '@/components/admin/PageHeader'
import { StatusBadge } from '@/components/admin/StatusBadge'
import { Combobox, MultiSelect } from '@/components/form/Combobox'
import { DatePicker } from '@/components/form/DatePicker'
import { FormField } from '@/components/form/FormField'
import { MoneyInput } from '@/components/form/MoneyInput'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { FieldGroup } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useColumnVisibility } from '@/hooks/use-column-visibility'
import { api } from '@/lib/api'
import { schemas, useZodForm } from '@/lib/forms'
import type { StatusTone } from '@/lib/status'
import { NotFoundPage } from '@/pages/auth/StatusPages'

// Δοκιμαστική σελίδα των κοινών components (form kit, DataTable, StatusBadge, MoneyText, Alert) για τα E2E tests,
// μέχρι να τα χρησιμοποιήσει μια πραγματική οθόνη. Υπάρχει μόνο όταν ο server έχει τα test endpoints (αλλιώς "δεν βρέθηκε").
export function KitPage() {
  const enabled = useQuery({ queryKey: ['test-enabled'], queryFn: () => api<void>('/api/test/enabled'), retry: false, staleTime: Infinity })

  if (enabled.isPending) return <Loader2 className="mx-auto mt-20 size-5 animate-spin text-muted-foreground" />
  if (enabled.isError) return <NotFoundPage />
  return <Kit />
}

// Δείγματα δεδομένων (όχι κείμενα του UI)
const clients = ['Acme Ltd', 'Blue Harbor SA', 'Cyclades Travel', 'Delta Logistics', 'Eurobank Branch 12', 'Fresh Bakery'].map((name, i) => ({
  value: `CL-00${41 + i}`,
  label: name,
}))
const tags = ['Website', 'Hosting', 'Domain', 'Support', 'Design'].map((tag) => ({ value: tag.toLowerCase(), label: tag }))

const schema = z.object({
  amount: schemas.required(schemas.amount()),
  dueOn: schemas.required(schemas.date()),
  client: schemas.required(z.string()),
  tags: z.array(z.string()).min(1),
  note: z.string().trim().min(3),
})

type Row = { id: string; name: string; amount: number | null; tone: StatusTone }

const rows: Row[] = [
  { id: 'r1', name: 'Cyclades Travel', amount: 1240, tone: 'danger' },
  { id: 'r2', name: 'Acme Ltd', amount: 400, tone: 'warning' },
  { id: 'r3', name: 'Blue Harbor SA', amount: null, tone: 'neutral' },
  { id: 'r4', name: 'Delta Logistics', amount: -50, tone: 'success' },
]

const tones: StatusTone[] = ['danger', 'warning', 'info', 'success', 'neutral', 'historical']

function Kit() {
  const { t } = useTranslation('admin')
  const [result, setResult] = useState<string | null>(null)
  const [sort, setSort] = useState('name')
  const columnVisibility = useColumnVisibility('kit')
  const form = useZodForm(schema, { amount: null, dueOn: null, client: null, tags: [], note: '' })

  const sorted = [...rows].sort((a, b) => a.name.localeCompare(b.name) * (sort === '-name' ? -1 : 1))
  const columns: Column<Row>[] = [
    { key: 'name', header: t('kit.table.name'), sortable: true, cell: (r) => <span className="font-medium">{r.name}</span> },
    {
      key: 'amount',
      header: t('kit.table.amount'),
      label: t('kit.table.amount'),
      className: 'text-right',
      cell: (r) => <MoneyText amount={r.amount} currency="EUR" credit={r.amount !== null && r.amount < 0} />,
    },
    { key: 'status', header: t('kit.table.status'), label: t('kit.table.status'), cell: (r) => <StatusBadge tone={r.tone}>{t(`kit.tones.${r.tone}`)}</StatusBadge> },
  ]

  return (
    <>
      <PageHeader title={t('kit.title')} description={t('kit.description')} />

      <div className="grid gap-4 xl:grid-cols-2">
        <SettingsCard title={t('kit.form.title')}>
          <form
            noValidate
            onSubmit={form.handleSubmit((values) => setResult(JSON.stringify(values)))}
            className="grid gap-5"
          >
            <FieldGroup>
              <FormField control={form.control} name="amount" id="kit-amount" label={t('kit.form.amount')}>
                {(field) => <MoneyInput {...field} currency="EUR" />}
              </FormField>
              <FormField control={form.control} name="dueOn" id="kit-due" label={t('kit.form.dueOn')}>
                {(field) => <DatePicker {...field} clearable />}
              </FormField>
              <FormField control={form.control} name="client" id="kit-client" label={t('kit.form.client')}>
                {(field) => <Combobox {...field} options={clients} />}
              </FormField>
              <FormField control={form.control} name="tags" id="kit-tags" label={t('kit.form.tags')}>
                {(field) => <MultiSelect {...field} options={tags} />}
              </FormField>
              <FormField control={form.control} name="note" id="kit-note" label={t('kit.form.note')}>
                {(field) => <Input {...field} />}
              </FormField>
            </FieldGroup>
            <div>
              <Button type="submit">{t('kit.form.submit')}</Button>
            </div>
            {result && (
              <pre data-testid="kit-result" className="overflow-x-auto rounded-lg bg-muted/40 p-3 text-xs">
                {result}
              </pre>
            )}
          </form>
        </SettingsCard>

        <div className="grid content-start gap-4">
          <SettingsCard title={t('kit.money.title')}>
            <div className="grid gap-2 text-sm">
              <div data-testid="money-plain">
                <MoneyText amount={1240} currency="EUR" />
              </div>
              <div data-testid="money-credit">
                <MoneyText amount={-50} currency="EUR" credit />
              </div>
              <div data-testid="money-unknown">
                <MoneyText amount={null} currency="EUR" />
              </div>
            </div>
          </SettingsCard>

          <SettingsCard title={t('kit.badges.title')}>
            <div data-testid="kit-badges" className="flex flex-wrap gap-2">
              {tones.map((tone) => (
                <StatusBadge key={tone} tone={tone}>
                  {t(`kit.tones.${tone}`)}
                </StatusBadge>
              ))}
            </div>
          </SettingsCard>

          <SettingsCard title={t('kit.alerts.title')}>
            <div className="grid gap-2">
              <Alert variant="info">
                <Info />
                <AlertTitle>{t('kit.alerts.info')}</AlertTitle>
              </Alert>
              <Alert variant="warning">
                <TriangleAlert />
                <AlertTitle>{t('kit.alerts.warning')}</AlertTitle>
              </Alert>
              <Alert variant="success">
                <CircleCheck />
                <AlertTitle>{t('kit.alerts.success')}</AlertTitle>
              </Alert>
              <Alert variant="error">
                <CircleAlert />
                <AlertTitle>{t('kit.alerts.error')}</AlertTitle>
                <AlertDescription>{t('kit.alerts.errorDescription')}</AlertDescription>
              </Alert>
            </div>
          </SettingsCard>
        </div>
      </div>

      <div className="mt-6 mb-3 flex items-center justify-between gap-2">
        <h2 className="font-semibold">{t('kit.table.title')}</h2>
        <ColumnsMenu columns={columns} visibility={columnVisibility.visibility} onToggle={columnVisibility.toggle} />
      </div>
      <DataTable
        columns={columns}
        rows={sorted}
        getRowId={(r) => r.id}
        rowLabel={(r) => r.name}
        empty={null}
        sort={sort}
        onSortChange={setSort}
        columnVisibility={columnVisibility.visibility}
        bulkActions={(ids, clear) => (
          <Button
            size="sm"
            variant="outline"
            onClick={() => {
              toast.success(t('kit.table.archived', { count: ids.length }))
              clear()
            }}
          >
            <Archive /> {t('kit.table.archive')}
          </Button>
        )}
      />
    </>
  )
}
