import { toast as sonnerToast } from "sonner"
import Toast, { ToastProps } from "./Toast"
import { ToastProvider } from "./ToastContext"

export const toast = (props: ToastProps) => {
  const { button, options, onCancel } = props

  return sonnerToast.custom(
    (id) => {
      const extendedButton = button
        ? {
            button: {
              ...button,
              onClick: () => {
                button.onClick()
                sonnerToast.dismiss(id)
              },
            },
            onCancel: () => {
              onCancel?.()
              sonnerToast.dismiss(id)
            },
          }
        : {}

      return (
        <ToastProvider toastId={id} dismiss={() => sonnerToast.dismiss(id)}>
          <Toast {...props} {...extendedButton} />
        </ToastProvider>
      )
    },
    { ...options },
  )
}

export default toast
