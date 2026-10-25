import { Link } from 'react-router';
import { Construction, SearchX } from 'lucide-react';
import { Button } from '@/components/ui/Button';
import { EmptyState, PageHeader } from '@/components/ui/Feedback';

/** Sections whose API is scheduled for a later phase. No sample data is shown in their place. */
export function ComingSoonPage({ title, phase }: { title: string; phase: string }) {
  return (
    <>
      <PageHeader title={title} />
      <EmptyState
        icon={<Construction className="size-10" />}
        title="هذا القسم قيد الإنشاء"
        description={`سيتم تفعيل «${title}» ضمن ${phase}.`}
        action={
          <Link to="/">
            <Button variant="outline">العودة للرئيسية</Button>
          </Link>
        }
      />
    </>
  );
}

export function NotFoundPage() {
  return (
    <EmptyState
      icon={<SearchX className="size-10" />}
      title="الصفحة غير موجودة"
      action={
        <Link to="/">
          <Button variant="outline">العودة للرئيسية</Button>
        </Link>
      }
    />
  );
}

export function ForbiddenPage() {
  return <EmptyState title="ليست لديك صلاحية للوصول إلى هذه الصفحة" description="راجع مدير النظام إذا كنت تحتاج هذه الصلاحية." />;
}
