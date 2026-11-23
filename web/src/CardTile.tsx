import type { Card, Label, Project } from "./api";
import { PRIORITY_NAMES } from "./api";
import { checklist, completedText, dueInfo } from "./format";
import { LabelChip } from "./LabelChip";
import { ProjectTag } from "./Projects";

interface Props {
  card: Card;
  labels: Map<number, Label>;
  projects: Map<number, Project>;
  /** Drawn under the pointer while dragging. */
  overlay?: boolean;
}

export function CardTile({ card, labels, projects, overlay }: Props) {
  const due = card.status === "done" ? null : dueInfo(card);
  const done = card.status === "done" ? completedText(card) : null;
  const tasks = checklist(card.description);
  const project = card.projectId === null ? undefined : projects.get(card.projectId);
  const cardLabels = card.labelIds.map((id) => labels.get(id)).filter((l): l is Label => !!l);
  const hasFooter = due || done || tasks || card.commentCount > 0 || (card.description.trim() && !tasks);

  return (
    <div className={`card-tile${overlay ? " overlay" : ""}`} data-priority={card.priority}>
      <div className="card-head">
        <span className="card-ident">
          <span className="card-number">#{card.id}</span>
          {project && <ProjectTag project={project} />}
        </span>
        {card.priority !== "medium" && <span className="priority">{PRIORITY_NAMES[card.priority]}</span>}
      </div>
      <div className="card-title">{card.title}</div>
      {cardLabels.length > 0 && (
        <div className="card-labels">
          {cardLabels.map((label) => (
            <LabelChip key={label.id} label={label} />
          ))}
        </div>
      )}
      {hasFooter && (
        <div className="card-foot">
          {due && <span className={`due ${due.state}`}>{due.text}</span>}
          {done && <span className="completed">{done}</span>}
          {tasks && (
            <span className={`tasks${tasks.done === tasks.total ? " all" : ""}`} title="Checklist items done">
              ☑ {tasks.done}/{tasks.total}
            </span>
          )}
          {card.commentCount > 0 && (
            <span className="comments" title="Comments">
              {card.commentCount === 1 ? "1 comment" : `${card.commentCount} comments`}
            </span>
          )}
          {!tasks && card.description.trim() && (
            <span className="has-notes" title="Has a description">
              Notes
            </span>
          )}
        </div>
      )}
    </div>
  );
}
