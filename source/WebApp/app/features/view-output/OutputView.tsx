import React from 'react';
import type { OutputItem } from '../../shared/resultTypes';
import type { LanguageName } from '../../shared/languages';
import { SimpleOutput } from './internal/SimpleOutput';
import { MemoryOutput } from './internal/MemoryOutput';
import { MemoryGraphOutput } from './internal/MemoryGraphOutput';
import { GroupOutput, InspectionGroup } from './internal/GroupOutput';
import { AnsiUp } from "ansi_up";

const ansi_up = new AnsiUp();

type Props = {
    output: ReadonlyArray<OutputItem|InspectionGroup>;

    sourceCode: string;
    sourceLanguage: LanguageName;
};

export const OutputView: React.FC<Props> = ({ output }) => {
    const renderItem = (item: OutputItem | InspectionGroup, index: number) => {
        if (typeof item === 'string')
        {
            const ansiHtml = ansi_up.ansi_to_html(item);
            return (<pre key={index} dangerouslySetInnerHTML={{ __html: ansiHtml }}></pre>);
        }

        switch (item.type) {
            case 'inspection:simple': return <SimpleOutput key={index} inspection={item} />;
            case 'inspection:memory': return <MemoryOutput key={index} inspection={item} />;
            case 'inspection:memory-graph': return <MemoryGraphOutput key={index} inspection={item} />;
            case 'inspection:group': return <GroupOutput key={index} group={item} />;
        }
    };

    return <div className="output result-content">
        {!output.length && <div className="output-empty">Completed — no output.</div>}
        {output.map(renderItem)}
    </div>;
};