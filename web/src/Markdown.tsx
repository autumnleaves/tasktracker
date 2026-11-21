import Markdown, { type Components } from "react-markdown";
import remarkGfm from "remark-gfm";

interface HastNode {
  type: string;
  tagName?: string;
  properties?: Record<string, unknown>;
  children?: HastNode[];
}

// Numbers each checklist checkbox in document order (data-task="0", "1", ...), so a click can be
// traced back to its line in the source (toggleChecklistItem).
function rehypeNumberTasks() {
  return (tree: HastNode) => {
    let n = 0;
    const visit = (node: HastNode) => {
      if (node.type === "element" && node.tagName === "input" && node.properties?.type === "checkbox") {
        node.properties.dataTask = n++;
      }
      node.children?.forEach(visit);
    };
    visit(tree);
  };
}

interface Props {
  source: string;
  /** Called with the checklist item's index when a checkbox is clicked. Read-only without it. */
  onToggleTask?: (index: number) => void;
}

/** Card descriptions: GitHub-flavoured Markdown, no raw HTML. Links open in a new tab. */
export function MarkdownView({ source, onToggleTask }: Props) {
  const components: Components = {
    a: ({ node: _node, ...props }) => <a {...props} target="_blank" rel="noreferrer" />,
    input: ({ node: _node, ...props }) => {
      if (props.type !== "checkbox") return <input {...props} />;
      const index = Number((props as Record<string, unknown>)["data-task"]);
      return (
        <input
          type="checkbox"
          checked={!!props.checked}
          disabled={!onToggleTask}
          onChange={() => onToggleTask?.(index)}
          onClick={(e) => e.stopPropagation()}
        />
      );
    },
  };
  return (
    <div className="markdown">
      <Markdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeNumberTasks]} components={components}>
        {source}
      </Markdown>
    </div>
  );
}
