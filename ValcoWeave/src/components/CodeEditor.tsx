import { useEffect, useRef } from 'react';
import { EditorView } from '@codemirror/view';
import type { EditorState } from '@codemirror/state';

interface Props {
  state: EditorState | null;
  viewRef: React.MutableRefObject<EditorView | null>;
  visible: boolean;
}

/** One CodeMirror view; tabs swap their EditorState in and out of it. */
export default function CodeEditor({ state, viewRef, visible }: Props) {
  const host = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!host.current || !state) return;
    if (!viewRef.current) {
      viewRef.current = new EditorView({ state, parent: host.current });
    } else if (viewRef.current.state !== state) {
      viewRef.current.setState(state);
    }
  }, [state, viewRef]);

  useEffect(() => {
    if (visible) viewRef.current?.requestMeasure();
  }, [visible, viewRef]);

  useEffect(
    () => () => {
      viewRef.current?.destroy();
      viewRef.current = null;
    },
    [viewRef],
  );

  return <div className="code-host" ref={host} style={{ display: state ? undefined : 'none' }} />;
}
