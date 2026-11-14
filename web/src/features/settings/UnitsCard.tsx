import { useMutation, useQueryClient } from '@tanstack/react-query';
import { lookupsApi } from '@/api/endpoints';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Switch } from '@/components/ui/Field';
import { Card } from '@/components/ui/Feedback';
import { useToast } from '@/components/ui/Toast';
import { errorMessage } from '@/lib/forms';
import { useLookups } from '@/lib/queries';

/** Units of measure: which are counted in whole numbers only (a piece, a set, a carton) so 0.5 of them is refused. */
export function UnitsCard() {
  const { can } = useAuth();
  const lookups = useLookups();
  const toast = useToast();
  const queryClient = useQueryClient();
  const save = useMutation({
    mutationFn: ({ id, value }: { id: number; value: boolean }) => lookupsApi.setUnitWholeNumbers(id, value),
    onSuccess: (u) => {
      void queryClient.invalidateQueries({ queryKey: ['lookups'] });
      toast.success(u.wholeNumbersOnly ? `«${u.name}» تُعدّ الآن بأعداد صحيحة فقط` : `«${u.name}» تقبل الكسور الآن`);
    },
    onError: (e) => toast.error(errorMessage(e)),
  });
  if (!can(P.ProductsManage) || !lookups.data) return null;

  return (
    <Card className="space-y-2 p-5">
      <h2 className="font-bold">وحدات القياس: أعداد صحيحة فقط</h2>
      <p className="text-sm text-muted">
        فعّل الوحدات التي لا تُباع ولا تُشترى بالكسور (قطعة، طقم، كرتونة…): يُرفض بعدها نصف قطعة في البيع والشراء والمرتجع والجرد وحركات المخزون.
        اترك المتر والكيلو واللتر بدون تفعيل.
      </p>
      <div className="divide-y divide-line">
        {lookups.data.units.map((u) => (
          <Switch key={u.id} label={u.name} checked={u.wholeNumbersOnly}
            description={u.wholeNumbersOnly ? 'أعداد صحيحة فقط' : 'تقبل الكسور'}
            onChange={(value) => save.mutate({ id: u.id, value })} />
        ))}
      </div>
    </Card>
  );
}
